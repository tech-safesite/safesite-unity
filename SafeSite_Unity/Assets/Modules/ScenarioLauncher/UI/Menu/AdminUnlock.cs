using UnityEngine;
using UnityEngine.EventSystems;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// Put on the SafeSite logo: <see cref="tapsNeeded"/> taps in quick succession open the admin PIN.
    /// Needs a Graphic with Raycast Target on the same object (the logo's Image).
    /// </summary>
    public sealed class AdminUnlock : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private LauncherMenu menu;
        [SerializeField] private int tapsNeeded = 5;
        [Tooltip("Seconds allowed between one tap and the next before the count starts again.")]
        [SerializeField] private float maxGap = 1.5f;

        private int _taps;
        private float _lastTap = float.NegativeInfinity;

        private void OnEnable() => _taps = 0;

        public void OnPointerClick(PointerEventData eventData)
        {
            var now = Time.unscaledTime;
            _taps = now - _lastTap <= maxGap ? _taps + 1 : 1;
            _lastTap = now;
            if (_taps < tapsNeeded || !menu)
                return;
            _taps = 0;
            menu.ShowAdminPin();
        }
    }
}
