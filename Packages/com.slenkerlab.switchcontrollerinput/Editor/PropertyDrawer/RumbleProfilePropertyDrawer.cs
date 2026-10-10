using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.Switch;

namespace SwitchControllerInput.Editor
{
    /// <summary>
    /// Custom property editor for <see cref="SwitchControllerRumbleProfile"/> with ranged sliders and a test button.
    /// </summary>
    [CustomPropertyDrawer(typeof(SwitchControllerRumbleProfile))]
    public class RumbleProfilePropertyDrawer : PropertyDrawer
    {
        // The profile has the same four fields for each side, named with the side as suffix
        private static readonly string[] sides = { "Left", "Right" };

        private const float amplitudeMin = 0f;
        private const float amplitudeMax = 1f;

        private const float labelHeight = 30f;
        private const float buttonHeight = 30f;
        private const float spacing = 10f;

        /// <inheritdoc/>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float y = position.y;
            foreach (string side in sides)
            {
                EditorGUI.LabelField(new Rect(position.x - 5, y, position.width, labelHeight), side + " hand: ", EditorStyles.boldLabel);
                y += labelHeight;

                y = SliderGUI(position, y, property.FindPropertyRelative("highBandFrequency" + side), SwitchControllerRumbleProfile.HighBandFrequencyMin, SwitchControllerRumbleProfile.HighBandFrequencyMax, "High-band frequency");
                y = SliderGUI(position, y, property.FindPropertyRelative("highBandAmplitude" + side), amplitudeMin, amplitudeMax, "High-band amplitude");
                y = SliderGUI(position, y, property.FindPropertyRelative("lowBandFrequency" + side), SwitchControllerRumbleProfile.LowBandFrequencyMin, SwitchControllerRumbleProfile.LowBandFrequencyMax, "Low-band frequency");
                y = SliderGUI(position, y, property.FindPropertyRelative("lowBandAmplitude" + side), amplitudeMin, amplitudeMax, "Low-band amplitude");
                y += spacing;
            }

            // Play button to test the rumble profile
            if (GUI.Button(new Rect(position.x - 5, y, position.width, buttonHeight), "Play"))
            {
                SwitchControllerRumbleProfile profile = SwitchControllerRumbleProfile.CreateEmpty();
                profile.highBandFrequencyLeft = property.FindPropertyRelative("highBandFrequencyLeft").floatValue;
                profile.highBandAmplitudeLeft = property.FindPropertyRelative("highBandAmplitudeLeft").floatValue;
                profile.lowBandFrequencyLeft = property.FindPropertyRelative("lowBandFrequencyLeft").floatValue;
                profile.lowBandAmplitudeLeft = property.FindPropertyRelative("lowBandAmplitudeLeft").floatValue;
                profile.highBandFrequencyRight = property.FindPropertyRelative("highBandFrequencyRight").floatValue;
                profile.highBandAmplitudeRight = property.FindPropertyRelative("highBandAmplitudeRight").floatValue;
                profile.lowBandFrequencyRight = property.FindPropertyRelative("lowBandFrequencyRight").floatValue;
                profile.lowBandAmplitudeRight = property.FindPropertyRelative("lowBandAmplitudeRight").floatValue;

                profile.PlayOn(SwitchProControllerNewHID.current, SwitchJoyConLHID.current, SwitchJoyConRHID.current);
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Draw the slider of one field at the given height, and return the height the next element starts at.
        /// </summary>
        private static float SliderGUI(Rect position, float y, SerializedProperty property, float min, float max, string label)
        {
            Rect rect = new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(property));
            EditorGUI.Slider(rect, property, min, max, label);
            return rect.yMax;
        }

        /// <inheritdoc/>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = buttonHeight;
            foreach (string side in sides)
            {
                height += labelHeight + spacing;
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("highBandFrequency" + side));
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("highBandAmplitude" + side));
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("lowBandFrequency" + side));
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("lowBandAmplitude" + side));
            }

            return height;
        }
    }
}
