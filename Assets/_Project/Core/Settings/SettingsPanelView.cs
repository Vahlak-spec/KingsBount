using UnityEngine;
using UnityEngine.UI;

namespace KingdomBouns.Core
{
    /// <summary>Вікно налаштувань. Один і той самий префаб можна класти і в меню, і в ігрову сцену.</summary>
    public class SettingsPanelView : MonoBehaviour
    {
        [field: SerializeField] public Slider MusicVolumeSlider { get; private set; }
        [field: SerializeField] public Slider EffectsVolumeSlider { get; private set; }
        [field: SerializeField] public Button CloseButton { get; private set; }
    }
}
