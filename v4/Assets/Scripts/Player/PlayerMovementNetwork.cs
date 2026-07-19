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
        public float YRotation;
    }

    public struct PlayerStateSnapshot : INetworkSerializeByMemcpy
    {
        public int Tick;
        public Vector3 Position;
        public float YRotation;
        public float VerticalVelocity;
        public bool Grounded;
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
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerMovementNetwork : NetworkBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float runSpeed = 5.4f; // Typical Valorant speed
        [SerializeField] private float walkSpeedMultiplier = 0.5f;
        [SerializeField] private float crouchSpeedMultiplier = 0.3f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Reconciliation")]
        [SerializeField] private float positionTolerance = 0.05f;

        [Header("Remote View")]
        [SerializeField] private float interpolationDelayTicks = 3f; // ~100ms @ 30Hz behind newest snapshot
        [SerializeField] private float interpolationSnapTicks = 15f; // gap beyond which the view teleports instead of fast-forwarding

        private const int MaxBufferedTicks = 128; // ~4s @ 30Hz safety cap if acks stop arriving
        private const int ServerInputBacklogThreshold = 2; // above this, burn 2 inputs per tick to catch up
        private const int MaxViewSnapshots = 64;

        /// <summary>
        /// The server's true position for this player — the last published
        /// snapshot, which covers both remote players (sim position, while the
        /// transform doubles as the host's smoothed view) and the host's own
        /// avatar (owner-simulated directly). Server-side consumers (hitbox
        /// history, shot origins) must use this, never transform.position.
        /// </summary>
        public Vector3 AuthoritativePosition => IsSpawned ? authoritativeState.Value.Position : transform.position;

        /// <summary>How many ticks in the past remote views are rendered (lag-comp rewind input).</summary>
        public float InterpolationDelayTicks => interpolationDelayTicks;

        private struct PredictedTick
        {
            public int Tick;
            public PlayerInputTick Input;
            public Vector3 ResultPosition;
            public float ResultVerticalVelocity;
            public bool ResultGrounded;
        }

        private CharacterController characterController;
        private PlayerController playerController;
        private PlayerRespawn playerRespawn;
        private Tactics.Sound.SoundEmitter soundEmitter;

        private readonly NetworkVariable<PlayerStateSnapshot> authoritativeState = new NetworkVariable<PlayerStateSnapshot>();

        // Owner prediction
        private readonly List<PredictedTick> pendingTicks = new List<PredictedTick>();
        private PlayerInputTick frozenInput;
        private bool hasFrozenInput;
        private float intervalSimTime;
        private PlayerInputTick prevInput1 = new PlayerInputTick { Tick = -1 };
        private PlayerInputTick prevInput2 = new PlayerInputTick { Tick = -1 };

        // Server simulation
        private readonly Queue<PlayerInputTick> serverInputQueue = new Queue<PlayerInputTick>();
        private bool serverInputStreamStarted;
        private int newestReceivedInputTick = -1;
        private Vector3 serverSimPosition;
        private int serverTeleportCount;
        private Tactics.Combat.Health health;

        // Last teleport count each consumer has processed.
        private int ownerTeleportCount;
        private int viewTeleportCount;

        // Remote view interpolation: non-owner instances render this player a fixed
        // few ticks in the past, playing back between buffered snapshots, so packet
        // gaps are bridged smoothly instead of causing the view to freeze then leap.
        private struct ViewSnapshot
        {
            public int Tick;
            public Vector3 Position;
            public float YRotation;
        }

        private readonly List<ViewSnapshot> viewBuffer = new List<ViewSnapshot>();
        private double viewTick; // playhead on the snapshot-tick timeline
        private bool viewInitialized;

        // Simulation state that must survive rewind: carried in every snapshot and
        // buffer entry. CharacterController.isGrounded reflects the last Move and is
        // corrupted by the enable-toggle teleports, so groundedness is tracked here.
        private float verticalVelocity;
        private bool simGrounded;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerController = GetComponent<PlayerController>();
            playerRespawn = GetComponent<PlayerRespawn>();
            soundEmitter = GetComponent<Tactics.Sound.SoundEmitter>();
            health = GetComponent<Tactics.Combat.Health>();
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
                        VerticalVelocity = verticalVelocity,
                        Grounded = simGrounded,
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
                VerticalVelocity = verticalVelocity,
                Grounded = simGrounded,
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
            verticalVelocity = 0f;
            simGrounded = false;
            serverTeleportCount++;

            PlayerStateSnapshot previous = authoritativeState.Value;
            authoritativeState.Value = new PlayerStateSnapshot
            {
                Tick = previous.Tick,
                Position = position,
                YRotation = previous.YRotation,
                VerticalVelocity = 0f,
                Grounded = false,
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

        private void Simulate(PlayerInputTick input, float dt, bool emitSound)
        {
            Quaternion rotation = Quaternion.Euler(0f, input.YRotation, 0f);
            Vector3 horizontal = PlayerMovementSimulation.ComputeHorizontalMove(
                input.Move, rotation, input.Walk, input.Crouch, runSpeed, walkSpeedMultiplier, crouchSpeedMultiplier);

            if (simGrounded)
            {
                verticalVelocity = -0.5f; // Keep grounded

                if (emitSound && soundEmitter != null && horizontal.sqrMagnitude > 0.01f && !input.Walk && !input.Crouch)
                {
                    soundEmitter.EmitMoveSound(true);
                }
            }
            else
            {
                verticalVelocity += gravity * dt;
            }

            Vector3 finalMove = horizontal + Vector3.up * verticalVelocity;
            characterController.Move(finalMove * dt);
            simGrounded = (characterController.collisionFlags & CollisionFlags.Below) != 0;
        }

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
                verticalVelocity = current.VerticalVelocity;
                simGrounded = current.Grounded;

                float dt = NetworkManager.LocalTime.FixedDeltaTime;
                for (int i = index + 1; i < pendingTicks.Count; i++)
                {
                    PredictedTick replay = pendingTicks[i];
                    Simulate(replay.Input, dt, emitSound: false);
                    replay.ResultPosition = transform.position;
                    replay.ResultVerticalVelocity = verticalVelocity;
                    replay.ResultGrounded = simGrounded;
                    pendingTicks[i] = replay;
                }
            }

            pendingTicks.RemoveRange(0, index + 1);
        }
    }

    internal static class PlayerMovementSimulation
    {
        public static Vector3 ComputeHorizontalMove(Vector2 input, Quaternion rotation, bool walking, bool crouching,
            float runSpeed, float walkSpeedMultiplier, float crouchSpeedMultiplier)
        {
            float speed = runSpeed;
            if (crouching) speed *= crouchSpeedMultiplier;
            else if (walking) speed *= walkSpeedMultiplier;

            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 direction = (forward * input.y + right * input.x).normalized;
            return direction * speed;
        }
    }
}
