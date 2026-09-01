using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Tactics.Player
{
    public struct PlayerInputTick : INetworkSerializeByMemcpy
    {
        public int Tick;
        public Vector2 Move;
        public bool Walk;
        public bool Crouch;
        public bool Jump;
        public bool Ads;
        public float YRotation;
    }

    public struct PlayerStateSnapshot : INetworkSerializeByMemcpy
    {
        public int Tick;
        public Vector3 Position;
        public float YRotation;
        public Vector3 HorizontalVelocity;
        public float VerticalVelocity;
        public bool Grounded;
        public bool IsCrouching;
        // Input-derived stance flags for server-side accuracy consumers: the
        // spread classifier judges what movement the player CHOSE (inputs), not
        // the speed those inputs happened to produce.
        public bool IsWalking;
        public bool IsAds;
        // Bumped on every server-side hard teleport (respawn). Consumers compare
        // against the last count they saw and snap instead of smoothing/reconciling,
        // since a teleport is not a misprediction.
        public int TeleportCount;
    }

    /// <summary>
    /// Server-authoritative movement with client-side prediction.
    ///
    /// Owner: simulates every rendered frame (smooth) using the input frozen at the
    /// last network tick, tops the interval up to exactly one tick of simulated time
    /// when the tick fires, records the result, and sends the input to the server.
    ///
    /// Server: replays each owner input as one fixed tick step and publishes the
    /// result via <see cref="authoritativeState"/>. The owner compares that against
    /// its own recorded prediction for the same input tick and rewinds+replays on
    /// divergence. Everyone else just renders a smoothed copy of the snapshot.
    ///
    /// IMPORTANT: all movement must go through <see cref="Simulate"/>. Any position
    /// change applied outside it (dashes, knockback, ...) will be treated as a
    /// misprediction and reverted by reconciliation.
    ///
    /// This file is organized by WHO runs each block, not just top-to-bottom by
    /// feature, since owner/server/everyone-else code is interleaved by necessity
    /// (a NetworkBehaviour is one class instantiated on every peer):
    ///   1. Inspector settings / public API / state fields (grouped by role below).
    ///   2. Unity lifecycle (Awake/OnNetworkSpawn/OnNetworkDespawn/Update) — dispatches
    ///      into the region-specific code below based on IsOwner/IsServer.
    ///   3. OWNER: INPUT CAPTURE & PREDICTION — only runs on the owning client.
    ///   4. SERVER: AUTHORITATIVE SIMULATION — only runs on the server.
    ///   5. SHARED PHYSICS STEP — the one method both of the above call into, so
    ///      client and server can never simulate differently.
    ///   6. REMOTE VIEW: INTERPOLATION — runs on every instance that is NOT
    ///      simulating this player locally (i.e. everyone except the owner).
    ///   7. OWNER: RECONCILIATION — only runs on the owning client.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerMovementNetwork : NetworkBehaviour
    {
        #region Inspector Settings

        [Header("Movement Settings")]
        [SerializeField] private float runSpeed = 5.4f; // Typical Valorant speed
        [SerializeField] private float walkSpeedMultiplier = 0.5f;
        [SerializeField] private float crouchSpeedMultiplier = 0.3f;
        [SerializeField] private float adsSpeedMultiplier = 0.76f; // stacks multiplicatively on walk/crouch
        [SerializeField] private float gravity = -9.81f;

        [Header("Crouch")]
        // Matches PlayerCrouchVisuals' crouchVisibilityHeight and the CPU vision
        // capsule's crouched height — all three must agree on how tall a crouched
        // player actually is.
        [SerializeField] private float crouchControllerHeight = 1.5f;

        [Header("Jump")]
        [SerializeField] private float jumpHeight = 0.9f; // apex height in meters, launch speed derived from gravity
        [SerializeField] private float airWishSpeed = 2.5f; // per-tick speed a single input can add toward its direction
        [SerializeField] private float airAcceleration = 10f; // how fast that addition approaches airWishSpeed
        [SerializeField] private float maxAirSpeedSafetyCap = 20f; // safety net only, not a gameplay cap (see PlayerMovementSimulation)

        [Header("Reconciliation")]
        [SerializeField] private float positionTolerance = 0.05f;

        [Header("Remote View")]
        [SerializeField] private float interpolationDelayTicks = 3f; // ~100ms @ 30Hz behind newest snapshot
        [SerializeField] private float interpolationSnapTicks = 15f; // gap beyond which the view teleports instead of fast-forwarding

        private const int MaxBufferedTicks = 128; // ~4s @ 30Hz safety cap if acks stop arriving
        private const int ServerInputBacklogThreshold = 2; // above this, burn 2 inputs per tick to catch up
        private const int MaxViewSnapshots = 64;

        #endregion

        #region Public API

        /// <summary>
        /// The server's true position for this player — the last published
        /// snapshot, which covers both remote players (sim position, while the
        /// transform doubles as the host's smoothed view) and the host's own
        /// avatar (owner-simulated directly). Server-side consumers (hitbox
        /// history, shot origins) must use this, never transform.position.
        /// </summary>
        public Vector3 AuthoritativePosition => IsSpawned ? authoritativeState.Value.Position : transform.position;

        /// <summary>Whether the server's authoritative copy of this player is airborne right now.</summary>
        public bool AuthoritativeGrounded => IsSpawned ? authoritativeState.Value.Grounded : true;

        /// <summary>How many ticks in the past remote views are rendered (lag-comp rewind input).</summary>
        public float InterpolationDelayTicks => interpolationDelayTicks;

        /// <summary>
        /// Effective crouch state for presentation (visual pose, visibility capsule).
        /// Owner reads its own zero-latency input; everyone else reads the last
        /// replicated snapshot.
        /// </summary>
        public bool CurrentIsCrouching => !IsSpawned ? false
            : IsOwner ? playerController.IsCrouching
            : authoritativeState.Value.IsCrouching;

        /// <summary>Crouch state of the server's authoritative copy (accuracy stance input).</summary>
        public bool AuthoritativeIsCrouching => IsSpawned && authoritativeState.Value.IsCrouching;

        /// <summary>ADS state of the server's authoritative copy (accuracy stance input).</summary>
        public bool AuthoritativeIsAds => IsSpawned && authoritativeState.Value.IsAds;

        /// <summary>
        /// Movement category of the server's authoritative copy, classified from
        /// the published snapshot — server-side accuracy consumers use this so a
        /// client can never claim it was standing still.
        /// </summary>
        public MovementState GetAuthoritativeMovementState()
        {
            if (!IsSpawned) return MovementState.Stationary;
            PlayerStateSnapshot snapshot = authoritativeState.Value;
            float horizontalSpeed = new Vector2(snapshot.HorizontalVelocity.x, snapshot.HorizontalVelocity.z).magnitude;
            return MovementClassifier.Classify(snapshot.Grounded, snapshot.IsCrouching, snapshot.IsWalking,
                horizontalSpeed);
        }

        #endregion

        #region Component Refs & Networked State

        private CharacterController characterController;
        private PlayerController playerController;
        private PlayerRespawn playerRespawn;
        private Tactics.Sound.SoundEmitter soundEmitter;
        private Tactics.Combat.Health health;

        private readonly NetworkVariable<PlayerStateSnapshot> authoritativeState = new NetworkVariable<PlayerStateSnapshot>();

        #endregion

        #region Owner: Prediction State

        private struct PredictedTick
        {
            public int Tick;
            public PlayerInputTick Input;
            public Vector3 ResultPosition;
            public Vector3 ResultHorizontalVelocity;
            public float ResultVerticalVelocity;
            public bool ResultGrounded;
        }

        private readonly List<PredictedTick> pendingTicks = new List<PredictedTick>();
        private PlayerInputTick frozenInput;
        private bool hasFrozenInput;
        private float intervalSimTime;
        private PlayerInputTick prevInput1 = new PlayerInputTick { Tick = -1 };
        private PlayerInputTick prevInput2 = new PlayerInputTick { Tick = -1 };

        // Last teleport count the owner's reconciliation has processed.
        private int ownerTeleportCount;

        #endregion

        #region Server: Simulation State

        private readonly Queue<PlayerInputTick> serverInputQueue = new Queue<PlayerInputTick>();
        private bool serverInputStreamStarted;
        private int newestReceivedInputTick = -1;
        private Vector3 serverSimPosition;
        private int serverTeleportCount;

        #endregion

        #region Remote View: Interpolation State

        // Non-owner instances render this player a fixed few ticks in the past,
        // playing back between buffered snapshots, so packet gaps are bridged
        // smoothly instead of causing the view to freeze then leap.
        private struct ViewSnapshot
        {
            public int Tick;
            public Vector3 Position;
            public float YRotation;
        }

        private readonly List<ViewSnapshot> viewBuffer = new List<ViewSnapshot>();
        private double viewTick; // playhead on the snapshot-tick timeline
        private bool viewInitialized;

        // Last teleport count the remote-view playback has processed.
        private int viewTeleportCount;

        #endregion

        #region Shared Physics State

        // Simulation state that must survive rewind: carried in every snapshot and
        // buffer entry. CharacterController.isGrounded reflects the last Move and is
        // corrupted by the enable-toggle teleports, so groundedness is tracked here.
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private bool simGrounded;

        // Captured once at Awake, before crouch ever mutates the live controller —
        // the fixed "standing" baseline that crouch height/center are derived from.
        private float standingControllerHeight;
        private Vector3 standingControllerCenter;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerController = GetComponent<PlayerController>();
            playerRespawn = GetComponent<PlayerRespawn>();
            soundEmitter = GetComponent<Tactics.Sound.SoundEmitter>();
            health = GetComponent<Tactics.Combat.Health>();

            standingControllerHeight = characterController.height;
            standingControllerCenter = characterController.center;
        }

        public override void OnNetworkSpawn()
        {
            characterController.enabled = IsOwner || IsServer;

            if (IsServer)
            {
                if (playerRespawn != null) playerRespawn.RespawnTo(playerRespawn.DefaultSpawnPosition);
                serverSimPosition = transform.position;

                authoritativeState.Value = new PlayerStateSnapshot
                {
                    Tick = NetworkManager.LocalTime.Tick,
                    Position = transform.position,
                    YRotation = transform.eulerAngles.y,
                    VerticalVelocity = 0f,
                    Grounded = false,
                    TeleportCount = 0
                };

                if (health != null) health.OnDeath += ServerHandleDeath;
            }

            if (IsOwner) NetworkManager.NetworkTickSystem.Tick += OnOwnerTick;
            else if (IsServer) NetworkManager.NetworkTickSystem.Tick += OnServerTick;

            authoritativeState.OnValueChanged += OnAuthoritativeStateChanged;

            ownerTeleportCount = authoritativeState.Value.TeleportCount;
            viewTeleportCount = authoritativeState.Value.TeleportCount;
            if (!IsOwner) AppendViewSnapshot(authoritativeState.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            {
                NetworkManager.NetworkTickSystem.Tick -= OnOwnerTick;
                NetworkManager.NetworkTickSystem.Tick -= OnServerTick;
            }

            authoritativeState.OnValueChanged -= OnAuthoritativeStateChanged;
            if (IsServer && health != null) health.OnDeath -= ServerHandleDeath;
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                // Frame-rate prediction: the tick handler only does bookkeeping.
                if (hasFrozenInput)
                {
                    float dt = Time.deltaTime;
                    Simulate(frozenInput, dt, emitSound: true);
                    intervalSimTime += dt;
                }
                return;
            }

            // Remote view — pure spectators have no simulation at all, and the
            // server's simulation of other players only advances at tick boundaries
            // (its true position lives in serverSimPosition, restored before each
            // step), so both play back the interpolated snapshot timeline.
            UpdateRemoteView();
        }

        #endregion

        #region Owner: Input Capture & Prediction
        // Everything in this region runs ONLY on the owning client.

        private void OnOwnerTick()
        {
            if (hasFrozenInput)
            {
                // Top the interval up to exactly one tick of simulated time — the
                // server integrates this input as a single fixed step, and comparing
                // positions is only meaningful if both sides simulated the same
                // duration. Overshoot (frame longer than a tick) carries over.
                float remaining = NetworkManager.LocalTime.FixedDeltaTime - intervalSimTime;
                if (remaining > 0f) Simulate(frozenInput, remaining, emitSound: false);
                intervalSimTime = remaining < 0f ? -remaining : 0f;

                pendingTicks.Add(new PredictedTick
                {
                    Tick = frozenInput.Tick,
                    Input = frozenInput,
                    ResultPosition = transform.position,
                    ResultHorizontalVelocity = horizontalVelocity,
                    ResultVerticalVelocity = verticalVelocity,
                    ResultGrounded = simGrounded
                });
                while (pendingTicks.Count > MaxBufferedTicks) pendingTicks.RemoveAt(0);

                if (IsServer)
                {
                    // Host's own avatar: it IS the authority, no RPC round-trip.
                    authoritativeState.Value = new PlayerStateSnapshot
                    {
                        Tick = frozenInput.Tick,
                        Position = transform.position,
                        YRotation = frozenInput.YRotation,
                        HorizontalVelocity = horizontalVelocity,
                        VerticalVelocity = verticalVelocity,
                        Grounded = simGrounded,
                        IsCrouching = frozenInput.Crouch,
                        IsWalking = frozenInput.Walk,
                        IsAds = frozenInput.Ads,
                        TeleportCount = serverTeleportCount
                    };
                }
            }

            frozenInput = new PlayerInputTick
            {
                Tick = NetworkManager.LocalTime.Tick,
                Move = playerController.MoveInput,
                Walk = playerController.IsWalking,
                Crouch = playerController.IsCrouching,
                Jump = playerController.ConsumeJumpQueued(),
                Ads = playerController.IsAiming,
                YRotation = transform.eulerAngles.y
            };
            hasFrozenInput = true;

            if (!IsServer)
            {
                // Send the two previous inputs along with the current one so a lost
                // or reordered unreliable packet doesn't leave a gap in the server's
                // input stream (it can tolerate two consecutive losses).
                SubmitInputServerRpc(frozenInput, prevInput1, prevInput2);
            }
            prevInput2 = prevInput1;
            prevInput1 = frozenInput;
        }

        #endregion

        #region Server: Authoritative Simulation
        // Everything in this region runs ONLY on the server. SubmitInputServerRpc is
        // *called* from the owner's OnOwnerTick above, but Rpc(SendTo.Server) means
        // its body executes on the server — it's the entry point where a client's
        // input arrives, not owner-side code.

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputServerRpc(PlayerInputTick current, PlayerInputTick previous1, PlayerInputTick previous2)
        {
            // Oldest first so redundant re-sends fill gaps in order.
            TryEnqueueInput(previous2);
            TryEnqueueInput(previous1);
            TryEnqueueInput(current);
        }

        private void TryEnqueueInput(PlayerInputTick input)
        {
            // Unreliable delivery is also unordered: never queue an input at or
            // behind one already accepted (also skips the Tick = -1 placeholders
            // sent during the first two ticks).
            if (input.Tick <= newestReceivedInputTick) return;
            newestReceivedInputTick = input.Tick;
            serverInputQueue.Enqueue(input);
        }

        private void OnServerTick()
        {
            // Build a small cushion before consuming the stream, so frame-timing
            // drift between the two processes doesn't starve the queue mid-stream.
            if (!serverInputStreamStarted)
            {
                if (serverInputQueue.Count < ServerInputBacklogThreshold) return;
                serverInputStreamStarted = true;
            }

            // Starved anyway (sustained loss): hold in place. Never simulate a
            // guessed input — the real one still arrives later and would make the
            // movement happen twice, which the owner sees as a forward jerk.
            // Backlogged: burn two per tick so no owner-predicted input is dropped.
            int toProcess = serverInputQueue.Count > ServerInputBacklogThreshold ? 2
                : Mathf.Min(1, serverInputQueue.Count);

            for (int i = 0; i < toProcess; i++)
            {
                SimulateServerStep(serverInputQueue.Dequeue());
            }
        }

        private void SimulateServerStep(PlayerInputTick input)
        {
            // Between ticks the transform doubles as the host's smoothed view of
            // this player (Update lerps it); restore the true simulated position
            // before stepping.
            if (transform.position != serverSimPosition)
            {
                characterController.enabled = false;
                transform.position = serverSimPosition;
                characterController.enabled = true;
            }

            transform.rotation = Quaternion.Euler(0f, input.YRotation, 0f);
            Simulate(input, NetworkManager.LocalTime.FixedDeltaTime, emitSound: false);
            serverSimPosition = transform.position;

            authoritativeState.Value = new PlayerStateSnapshot
            {
                Tick = input.Tick,
                Position = transform.position,
                YRotation = input.YRotation,
                HorizontalVelocity = horizontalVelocity,
                VerticalVelocity = verticalVelocity,
                Grounded = simGrounded,
                IsCrouching = input.Crouch,
                IsWalking = input.Walk,
                IsAds = input.Ads,
                TeleportCount = serverTeleportCount
            };
        }

        /// <summary>
        /// Server-side hard teleport (respawn). This is the one sanctioned way to
        /// move a player outside <see cref="Simulate"/>: it bumps TeleportCount so
        /// the owner snaps-and-clears its prediction buffer instead of treating
        /// the jump as a misprediction, and remote views cut instead of gliding
        /// across the map.
        /// </summary>
        public void ServerTeleport(Vector3 position)
        {
            if (!IsServer) return;

            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = true;
            serverSimPosition = position;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            simGrounded = false;
            serverTeleportCount++;

            PlayerStateSnapshot previous = authoritativeState.Value;
            authoritativeState.Value = new PlayerStateSnapshot
            {
                Tick = previous.Tick,
                Position = position,
                YRotation = previous.YRotation,
                HorizontalVelocity = Vector3.zero,
                VerticalVelocity = 0f,
                Grounded = false,
                IsCrouching = false,
                IsWalking = false,
                IsAds = false,
                TeleportCount = serverTeleportCount
            };

            // Wipe the lag-comp history so rewound shots can't hit the spot this
            // player teleported away from.
            GetComponent<Tactics.Combat.HitboxHistory>()?.ServerReset();
        }

        private void ServerHandleDeath()
        {
            // Instant deathmatch-style respawn; proper death states (spectating,
            // round flow) arrive with the round-system milestone.
            StartCoroutine(ServerRespawnNextFrame());
        }

        private System.Collections.IEnumerator ServerRespawnNextFrame()
        {
            // One-frame delay so other OnDeath reactions (spike drop) still see
            // the corpse position before the teleport moves it.
            yield return null;
            ServerTeleport(playerRespawn != null ? playerRespawn.DefaultSpawnPosition : transform.position);
            if (health != null) health.ServerRevive();
        }

        #endregion

        #region Shared Physics Step
        // Called from all three simulation paths above — owner prediction
        // (OnOwnerTick / Update), host-direct-authority, and server replay
        // (SimulateServerStep) — so client and server can never simulate
        // differently: there is exactly one movement formula.

        private void Simulate(PlayerInputTick input, float dt, bool emitSound)
        {
            ApplyCrouchCollider(input.Crouch);

            Quaternion rotation = Quaternion.Euler(0f, input.YRotation, 0f);
            Vector3 desired = PlayerMovementSimulation.ComputeHorizontalMove(
                input.Move, rotation, input.Walk, input.Crouch, input.Ads,
                runSpeed, walkSpeedMultiplier, crouchSpeedMultiplier, adsSpeedMultiplier);

            bool justJumped = false;

            if (simGrounded && !input.Jump)
            {
                // Normal grounded movement: instant control, unchanged from before jumping
                // existed. Landing here without immediately re-jumping is also where a bhop
                // chain breaks — momentum resets to plain run speed.
                horizontalVelocity = desired;
                verticalVelocity = -0.5f; // Keep grounded

                if (emitSound && soundEmitter != null && horizontalVelocity.sqrMagnitude > 0.01f && !input.Walk && !input.Crouch)
                {
                    soundEmitter.EmitMoveSound(true);
                }
            }
            else
            {
                if (simGrounded && input.Jump)
                {
                    // Jump launch — fresh or chained. horizontalVelocity is left untouched:
                    // on a fresh jump it already holds last tick's grounded speed (the
                    // momentum carried into the jump); on a same-tick landing+rejump (a
                    // successful bhop) it still holds the air speed from before landing,
                    // uneroded — this is what makes chaining possible.
                    verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
                    simGrounded = false;
                    justJumped = true;
                }
                else
                {
                    verticalVelocity += gravity * dt;
                }

                // Air-accelerate (Quake/Source bhop formula): input only adds speed toward
                // wishDir up to airWishSpeed relative to velocity's current component along
                // wishDir, not toward a fixed cap on total magnitude — so speed can compound
                // across chained, well-steered jumps instead of being capped per jump.
                if (desired.sqrMagnitude > 0.0001f)
                {
                    Vector3 wishDir = desired.normalized;
                    float currentSpeedAlongWish = Vector3.Dot(horizontalVelocity, wishDir);
                    float addSpeed = airWishSpeed - currentSpeedAlongWish;
                    if (addSpeed > 0f)
                    {
                        float accelSpeed = Mathf.Min(airAcceleration * airWishSpeed * dt, addSpeed);
                        horizontalVelocity += wishDir * accelSpeed;
                    }
                }

                // Safety net only, not a design cap — guards against runaway float growth.
                if (horizontalVelocity.magnitude > maxAirSpeedSafetyCap)
                    horizontalVelocity = horizontalVelocity.normalized * maxAirSpeedSafetyCap;
            }

            Vector3 finalMove = horizontalVelocity + Vector3.up * verticalVelocity;
            characterController.Move(finalMove * dt);
            if (!justJumped)
                simGrounded = (characterController.collisionFlags & CollisionFlags.Below) != 0;
        }

        /// <summary>
        /// Shrinks/restores the CharacterController to match the crouch pose, keeping
        /// the capsule's bottom fixed at the standing ground-contact point so crouching
        /// never moves the feet. Called from Simulate() (not LateUpdate/visuals) so the
        /// owner's prediction and the server's replay resize the collider at the exact
        /// same point in the simulation, before the Move() call that might depend on it
        /// (e.g. a future low opening only a crouched player fits through).
        /// </summary>
        private void ApplyCrouchCollider(bool crouching)
        {
            float targetHeight = crouching ? crouchControllerHeight : standingControllerHeight;
            if (Mathf.Approximately(characterController.height, targetHeight))
                return;

            float bottomLocalY = standingControllerCenter.y - standingControllerHeight * 0.5f;
            characterController.height = targetHeight;
            characterController.center = new Vector3(
                standingControllerCenter.x,
                bottomLocalY + targetHeight * 0.5f,
                standingControllerCenter.z);
        }

        #endregion

        #region Remote View: Interpolation
        // Runs on every instance that is NOT simulating this player locally — i.e.
        // everyone except the owner (including the server's own view of players it
        // doesn't own, and every spectator/other client).

        private void UpdateRemoteView()
        {
            if (viewBuffer.Count == 0) return;

            ViewSnapshot newest = viewBuffer[viewBuffer.Count - 1];
            double targetTick = newest.Tick - interpolationDelayTicks;

            if (!viewInitialized || System.Math.Abs(targetTick - viewTick) > interpolationSnapTicks)
            {
                viewTick = targetTick;
                viewInitialized = true;
            }
            else
            {
                // Play forward at tick rate, gently speeding up/slowing down to track
                // the target delay so brief gaps fast-forward instead of teleporting.
                double drift = targetTick - viewTick;
                double speed = System.Math.Max(0.5, System.Math.Min(2.0, 1.0 + drift * 0.1));
                viewTick += speed * Time.deltaTime / NetworkManager.LocalTime.FixedDeltaTime;
            }
            if (viewTick > newest.Tick) viewTick = newest.Tick;

            // Drop snapshots the playhead has fully passed.
            while (viewBuffer.Count >= 2 && viewBuffer[1].Tick <= viewTick) viewBuffer.RemoveAt(0);

            ViewSnapshot from = viewBuffer[0];
            Vector3 position = from.Position;
            float yRotation = from.YRotation;
            if (viewBuffer.Count >= 2 && viewTick > from.Tick)
            {
                ViewSnapshot to = viewBuffer[1];
                float t = (float)((viewTick - from.Tick) / (to.Tick - from.Tick));
                position = Vector3.Lerp(from.Position, to.Position, t);
                yRotation = Mathf.LerpAngle(from.YRotation, to.YRotation, t);
            }

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
        }

        private void AppendViewSnapshot(PlayerStateSnapshot snapshot)
        {
            if (viewBuffer.Count > 0 && snapshot.Tick <= viewBuffer[viewBuffer.Count - 1].Tick) return;
            viewBuffer.Add(new ViewSnapshot
            {
                Tick = snapshot.Tick,
                Position = snapshot.Position,
                YRotation = snapshot.YRotation
            });
            while (viewBuffer.Count > MaxViewSnapshots) viewBuffer.RemoveAt(0);
        }

        #endregion

        #region Owner: Reconciliation
        // Runs only on the owning client (the host early-returns since it already
        // IS the authority for its own player, and non-owner instances take the
        // remote-view branch instead — see the top of the method).

        private void OnAuthoritativeStateChanged(PlayerStateSnapshot previous, PlayerStateSnapshot current)
        {
            if (!IsOwner)
            {
                // A teleport is a cut, not motion — restart the playback timeline.
                if (current.TeleportCount != viewTeleportCount)
                {
                    viewTeleportCount = current.TeleportCount;
                    viewBuffer.Clear();
                    viewInitialized = false;
                }
                AppendViewSnapshot(current);
                return;
            }
            if (IsServer) return; // host already IS the authority for its own player

            if (current.TeleportCount != ownerTeleportCount)
            {
                // Server-side teleport (respawn): snap to it and abandon every
                // prediction made before it — those inputs belong to a position
                // that no longer exists.
                ownerTeleportCount = current.TeleportCount;
                characterController.enabled = false;
                transform.position = current.Position;
                characterController.enabled = true;
                horizontalVelocity = current.HorizontalVelocity;
                verticalVelocity = current.VerticalVelocity;
                simGrounded = current.Grounded;
                pendingTicks.Clear();
                return;
            }

            int index = -1;
            for (int i = 0; i < pendingTicks.Count; i++)
            {
                if (pendingTicks[i].Tick == current.Tick) { index = i; break; }
            }
            if (index < 0) return; // ack for a trimmed tick, or a dead-reckoned repeat

            PredictedTick acked = pendingTicks[index];
            if (Vector3.Distance(acked.ResultPosition, current.Position) > positionTolerance)
            {
                characterController.enabled = false;
                transform.position = current.Position;
                characterController.enabled = true;
                horizontalVelocity = current.HorizontalVelocity;
                verticalVelocity = current.VerticalVelocity;
                simGrounded = current.Grounded;

                float dt = NetworkManager.LocalTime.FixedDeltaTime;
                for (int i = index + 1; i < pendingTicks.Count; i++)
                {
                    PredictedTick replay = pendingTicks[i];
                    Simulate(replay.Input, dt, emitSound: false);
                    replay.ResultPosition = transform.position;
                    replay.ResultHorizontalVelocity = horizontalVelocity;
                    replay.ResultVerticalVelocity = verticalVelocity;
                    replay.ResultGrounded = simGrounded;
                    pendingTicks[i] = replay;
                }
            }

            pendingTicks.RemoveRange(0, index + 1);
        }

        #endregion
    }

    internal static class PlayerMovementSimulation
    {
        public static Vector3 ComputeHorizontalMove(Vector2 input, Quaternion rotation, bool walking, bool crouching, bool ads,
            float runSpeed, float walkSpeedMultiplier, float crouchSpeedMultiplier, float adsSpeedMultiplier)
        {
            float speed = runSpeed;
            if (crouching) speed *= crouchSpeedMultiplier;
            else if (walking) speed *= walkSpeedMultiplier;
            if (ads) speed *= adsSpeedMultiplier;

            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 direction = (forward * input.y + right * input.x).normalized;
            return direction * speed;
        }
    }
}
