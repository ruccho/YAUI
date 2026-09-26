using UnityEngine;

namespace Yaui.Samples.Controls
{
    /// <summary>Shows the values of the controls of the sample in labels.</summary>
    public class ControlsSample : MonoBehaviour
    {
        [SerializeField] YauiButton button;
        [SerializeField] YauiText buttonLabel;
        [SerializeField] YauiToggle checkbox;
        [SerializeField] YauiToggleGroup sizes;
        [SerializeField] YauiSlider slider;
        [SerializeField] YauiText sliderLabel;
        [SerializeField] YauiInputField nameField;
        [SerializeField] YauiDropdown fruit;
        [SerializeField] YauiText status;

        int _presses;

        void OnEnable()
        {
            button.OnClick.AddListener(OnPressed);
            checkbox.OnValueChanged.AddListener(OnChanged);
            slider.OnValueChanged.AddListener(OnSlider);
            nameField.OnValueChanged.AddListener(OnName);
            fruit.OnValueChanged.AddListener(OnFruit);
            foreach (var toggle in sizes.GetComponentsInChildren<YauiToggle>())
            {
                toggle.OnValueChanged.AddListener(OnChanged);
            }
        }

        // After the toggles joined their group in their own OnEnable.
        void Start()
        {
            sliderLabel.Text = slider.Value.ToString("0");
            UpdateStatus();
        }

        void OnDisable()
        {
            button.OnClick.RemoveListener(OnPressed);
            checkbox.OnValueChanged.RemoveListener(OnChanged);
            slider.OnValueChanged.RemoveListener(OnSlider);
            nameField.OnValueChanged.RemoveListener(OnName);
            fruit.OnValueChanged.RemoveListener(OnFruit);
            foreach (var toggle in sizes.GetComponentsInChildren<YauiToggle>())
            {
                toggle.OnValueChanged.RemoveListener(OnChanged);
            }
        }

        void OnPressed()
        {
            _presses++;
            buttonLabel.Text = $"Pressed {_presses}";
        }

        void OnChanged(bool _) => UpdateStatus();

        void OnSlider(float value)
        {
            sliderLabel.Text = value.ToString("0");
            UpdateStatus();
        }

        void OnName(string _) => UpdateStatus();

        void OnFruit(int _) => UpdateStatus();

        void UpdateStatus()
        {
            var size = sizes.ActiveToggle != null ? sizes.ActiveToggle.name : "none";
            var name = string.IsNullOrEmpty(nameField.Text) ? "nobody" : nameField.Text;
            status.Text = $"Hello, {name}. Size: {size}, sound: {(checkbox.IsOn ? "on" : "off")}, " +
                          $"volume: {slider.Value:0}, fruit: {fruit.Options[fruit.Value].text}";
        }
    }
}
