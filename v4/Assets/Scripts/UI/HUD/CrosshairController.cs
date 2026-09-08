using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Tactics.Weapons;
// UIElements has its own Cursor type (element styling); we mean the OS pointer.
using Cursor = UnityEngine.Cursor;

namespace Tactics.UI
{
    /// <summary>
    /// Everything a future settings menu should be able to change about the
    /// crosshair lives here, so customization is a matter of mutating this
    /// object — the drawing code reads it live every repaint.
    /// </summary>
    [System.Serializable]
    public class CrosshairSettings
    {
        public Color color = Color.white;
        public Color outlineColor = new Color(0f, 0f, 0f, 0.8f);
        public float outlineThickness = 1f;

        [Header("Plus (single-bullet weapons)")]
        public float plusLength = 6f;
        public float plusThickness = 2f;
        public float plusGap = 3f;

        [Header("Circle (shotguns)")]
        public float circleThickness = 2f;
        public bool showCenterDot = true;
        public float dotRadius = 1.5f;
    }

    /// <summary>
    /// Replaces the OS cursor with a drawn crosshair: a simple "+" for
    /// single-bullet weapons, and — whenever the CURRENT trigger pull would throw
    /// more than one pellet, so a shotgun always and the Classic while right click
    /// is held — a circle whose radius is the pellet footprint at the aim point:
    /// the same distance·tan(spread) projection the server applies to pellets,
    /// fed from the owner-local spread mirror, so the circle exactly bounds
    /// where this shot's pellets can land. Purely cosmetic and owner-local.
    /// </summary>
    // After TopDownCamera (150): the crosshair must see this frame's camera pose
    // and warped cursor position, not last frame's.
    [DefaultExecutionOrder(160)]
    public class CrosshairController : MonoBehaviour
    {
        /// <summary>Set while a menu needs the OS cursor (buy menu); the
        /// crosshair hides itself and hands the pointer back.</summary>
        public static bool MenuOpen;

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private CrosshairSettings settings = new CrosshairSettings();

        private CrosshairElement element;
        private WeaponController weaponController;
        private Transform playerTransform;
        private Tactics.Camera.TopDownCamera topDownCamera;

        private void OnEnable()
        {
            element = new CrosshairElement(settings);
            // Last child of the document root → drawn on top of the HUD.
            uiDocument.rootVisualElement.Add(element);
        }

        private void OnDisable()
        {
            element?.RemoveFromHierarchy();
            element = null;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (weaponController == null) FindPlayer();

            bool show = weaponController != null && !MenuOpen && element != null && element.panel != null;
            Cursor.visible = !show;
            if (element != null) element.visible = show;
            if (!show) return;

            // Mouse position is bottom-left origin; UI Toolkit screen space is
            // top-left, so flip before the panel-scale conversion.
            Vector2 mouseScreen = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            // A cursor warp (ADS compensation) doesn't land in Mouse.current
            // until next frame's input update — draw at the warped position.
            Vector2? warped = topDownCamera != null ? topDownCamera.WarpedCursorScreenPosThisFrame : null;
            if (warped.HasValue) mouseScreen = warped.Value;
            Vector2 panelCenter = RuntimePanelUtils.ScreenToPanel(element.panel,
                new Vector2(mouseScreen.x, Screen.height - mouseScreen.y));

            WeaponData weapon = weaponController.CurrentWeapon;
            bool drawCircle = weapon != null && weaponController.CurrentPelletCount > 1;
            float radiusPanel = 0f;

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            // No aim point means Shoot() would refuse to fire there too — fall
            // back to the plain + rather than a meaningless circle.
            if (drawCircle && cam != null && weaponController.TryGetAimPoint(out Vector3 aimPoint))
            {
                Vector3 toAim = aimPoint - playerTransform.position;
                toAim.y = 0f;
                float radiusMeters = SpreadCalculator.ComputeSpreadWorldRadiusMeters(
                    toAim.magnitude, weaponController.CurrentSpreadDegrees,
                    weaponController.CurrentSpreadDistanceExponent);

                // Project the aim point and a point one radius away along any
                // horizontal axis; their panel distance is the radius in panel
                // units — exact under camera zoom, yaw, and perspective.
                Vector3 right = cam.transform.right;
                right.y = 0f;
                right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
                Vector2 centerPanel = RuntimePanelUtils.CameraTransformWorldToPanel(element.panel, aimPoint, cam);
                Vector2 edgePanel = RuntimePanelUtils.CameraTransformWorldToPanel(element.panel,
                    aimPoint + right * radiusMeters, cam);
                radiusPanel = Vector2.Distance(centerPanel, edgePanel);
            }
            else
            {
                drawCircle = false;
            }

            element.SetState(panelCenter, drawCircle, radiusPanel);
        }

        private void FindPlayer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return;
            var player = nm.LocalClient.PlayerObject.gameObject;
            weaponController = player.GetComponent<WeaponController>();
            playerTransform = player.transform;
            if (UnityEngine.Camera.main != null)
                topDownCamera = UnityEngine.Camera.main.GetComponent<Tactics.Camera.TopDownCamera>();
        }
    }

    /// <summary>
    /// Full-panel overlay that paints the crosshair with Painter2D. Ignores
    /// picking so it never blocks clicks on menus underneath.
    /// </summary>
    public class CrosshairElement : VisualElement
    {
        private readonly CrosshairSettings settings;
        private Vector2 center;
        private bool drawCircle;
        private float circleRadius;

        public CrosshairElement(CrosshairSettings settings)
        {
            this.settings = settings;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
        }

        public void SetState(Vector2 panelCenter, bool circle, float radiusPanel)
        {
            center = panelCenter;
            drawCircle = circle;
            circleRadius = Mathf.Max(radiusPanel, 2f); // stays visible when aiming at your own feet
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Painter2D painter = mgc.painter2D;
            painter.lineCap = LineCap.Butt;

            // Outline pass first (same shapes, wider stroke) so the crosshair
            // stays readable on bright ground.
            float outline = settings.outlineThickness * 2f;
            if (drawCircle)
            {
                if (outline > 0f) StrokeCircle(painter, settings.outlineColor, settings.circleThickness + outline, circleRadius);
                StrokeCircle(painter, settings.color, settings.circleThickness, circleRadius);
                if (settings.showCenterDot)
                {
                    if (outline > 0f) FillDot(painter, settings.outlineColor, settings.dotRadius + settings.outlineThickness);
                    FillDot(painter, settings.color, settings.dotRadius);
                }
            }
            else
            {
                // Filled rectangles, not strokes: Painter2D stroke end-caps
                // proved unreliable for outlining the arm ends, and a fill
                // expanded on every side can't miss an edge.
                if (settings.outlineThickness > 0f) FillPlus(painter, settings.outlineColor, settings.outlineThickness);
                FillPlus(painter, settings.color, 0f);
            }
        }

        /// <summary>Four arm rectangles, each grown by <paramref name="expand"/>
        /// on all sides (the outline pass draws the grown version underneath).</summary>
        private void FillPlus(Painter2D painter, Color color, float expand)
        {
            float gap = Mathf.Max(0f, settings.plusGap - expand);
            float end = settings.plusGap + settings.plusLength + expand;
            float half = settings.plusThickness * 0.5f + expand;

            FillRect(painter, color, center.x - end, center.y - half, center.x - gap, center.y + half);  // left
            FillRect(painter, color, center.x + gap, center.y - half, center.x + end, center.y + half);  // right
            FillRect(painter, color, center.x - half, center.y - end, center.x + half, center.y - gap);  // up
            FillRect(painter, color, center.x - half, center.y + gap, center.x + half, center.y + end);  // down
        }

        private static void FillRect(Painter2D painter, Color color, float xMin, float yMin, float xMax, float yMax)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(xMin, yMin));
            painter.LineTo(new Vector2(xMax, yMin));
            painter.LineTo(new Vector2(xMax, yMax));
            painter.LineTo(new Vector2(xMin, yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private void StrokeCircle(Painter2D painter, Color color, float thickness, float radius)
        {
            painter.strokeColor = color;
            painter.lineWidth = thickness;
            painter.BeginPath();
            painter.Arc(center, radius, 0f, 360f);
            painter.ClosePath();
            painter.Stroke();
        }

        private void FillDot(Painter2D painter, Color color, float radius)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, 0f, 360f);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
