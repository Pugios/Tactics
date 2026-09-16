using UnityEngine;

namespace Tactics.Core
{
    /// <summary>
    /// A full-screen menu that takes the mouse away from the game while open.
    /// </summary>
    public interface IGameMenu
    {
        /// <summary>
        /// Whether the player stands still while this menu is open. The Esc menu
        /// does (like planting); the buy menu doesn't, since walking while
        /// shopping is normal in Valorant.
        /// </summary>
        bool FreezesPlayer { get; }

        /// <summary>
        /// Esc was pressed while this menu is the open one: back out one level —
        /// a dialog inside the menu first, otherwise the menu itself.
        /// </summary>
        void Cancel();
    }

    /// <summary>
    /// The one menu currently open, if any. Menus never stack: opening one while
    /// another is up is refused, so every "is a menu open" question has a single
    /// owner to ask and closing one menu can never un-hide the crosshair from
    /// under another. Gameplay reads <see cref="IsOpen"/> to stop treating the
    /// mouse as aim, and <see cref="FreezesPlayer"/> to drop movement input.
    /// </summary>
    public static class MenuState
    {
        public static IGameMenu Current { get; private set; }

        public static bool IsOpen => Current != null;

        public static bool FreezesPlayer => Current != null && Current.FreezesPlayer;

        /// <summary>Claims the screen for <paramref name="menu"/>. False while a different menu is open.</summary>
        public static bool TryOpen(IGameMenu menu)
        {
            if (menu == null) return false;
            if (Current != null && !ReferenceEquals(Current, menu)) return false;
            Current = menu;
            return true;
        }

        /// <summary>Releases the screen; a no-op unless <paramref name="menu"/> is the open one.</summary>
        public static void NotifyClosed(IGameMenu menu)
        {
            if (ReferenceEquals(Current, menu)) Current = null;
        }

        // Domain reload is disabled on entering Play Mode, so statics survive
        // between sessions; a menu left open when play stopped must not linger.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
