using UnityEngine;
using UnityEngine.UI;

namespace KingdomBouns.Core
{
    public class MainMenuView : MonoBehaviour
    {
        [field: SerializeField] public Button ContinueButton { get; private set; }
        [field: SerializeField] public Button NewGameButton { get; private set; }
        [field: SerializeField] public Button SettingsButton { get; private set; }
        [field: SerializeField] public Button QuitButton { get; private set; }
        [field: SerializeField] public SettingsPanelView SettingsPanel { get; private set; }
    }
}
