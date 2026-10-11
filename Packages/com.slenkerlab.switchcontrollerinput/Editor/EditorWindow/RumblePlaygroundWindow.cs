using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.Switch;

namespace SwitchControllerInput.Editor
{
    /// <summary>
    /// Editor window allowing to test different values for the rumble profiles, as well as files.
    /// </summary>
    public class RumblePlaygroundWindow : EditorWindow
    {
        private bool profilePlaygroundFold = true;
        private SwitchControllerRumbleProfile rumbleProfile = SwitchControllerRumbleProfile.CreateNeutral();
        private bool playProfileToggle = false;
        private bool playProfileToggledLastFrame = false;
        private bool dataSheetFold = false;

        [MenuItem("Window/Switch Controller Debugger/Rumble Playground")]
        public static void ShowWindow()
        {
            RumblePlaygroundWindow wnd = EditorWindow.GetWindow<RumblePlaygroundWindow>();
            wnd.titleContent = new GUIContent("Joycon Rumble Playground");
        }

        private void OnGUI()
        {
            profilePlaygroundFold = RumbleProfilePlayground(profilePlaygroundFold);
            dataSheetFold = RumbleDataSheetPlayer(dataSheetFold);
        }

        private bool RumbleProfilePlayground(bool fold)
        {
            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Controllers precise rumble");

            if (fold)
            {
                bool hasProController = SwitchProControllerNewHID.current != null;

                RumbleSideGUI("Left controller", hasProController || SwitchJoyConLHID.current != null,
                    ref rumbleProfile.highBandFrequencyLeft, ref rumbleProfile.highBandAmplitudeLeft,
                    ref rumbleProfile.lowBandFrequencyLeft, ref rumbleProfile.lowBandAmplitudeLeft);

                EditorGUILayout.Separator();

                RumbleSideGUI("Right controller", hasProController || SwitchJoyConRHID.current != null,
                    ref rumbleProfile.highBandFrequencyRight, ref rumbleProfile.highBandAmplitudeRight,
                    ref rumbleProfile.lowBandFrequencyRight, ref rumbleProfile.lowBandAmplitudeRight);

                EditorGUILayout.Separator();

                playProfileToggle = GUILayout.Toggle(playProfileToggle, "Play the defined vibration");
                playProfileToggledLastFrame |= playProfileToggle;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            return fold;
        }

        /// <summary>
        /// Sliders for the rumble of one side, or a notice if no controller is connected for it.
        /// </summary>
        private static void RumbleSideGUI(string title, bool connected,
            ref float highBandFrequency, ref float highBandAmplitude, ref float lowBandFrequency, ref float lowBandAmplitude)
        {
            if (!connected)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(title, EditorStyles.boldLabel);
                GUILayout.Label("(disconnected)");
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.Label(title + ": ", EditorStyles.boldLabel);
            highBandFrequency = EditorGUILayout.Slider("High band frequency: ", highBandFrequency, SwitchControllerRumbleProfile.HighBandFrequencyMin, SwitchControllerRumbleProfile.HighBandFrequencyMax);
            highBandAmplitude = EditorGUILayout.Slider("High band amplitude: ", highBandAmplitude, 0, 1);
            lowBandFrequency = EditorGUILayout.Slider("Low band frequency: ", lowBandFrequency, SwitchControllerRumbleProfile.LowBandFrequencyMin, SwitchControllerRumbleProfile.LowBandFrequencyMax);
            lowBandAmplitude = EditorGUILayout.Slider("Low band amplitude: ", lowBandAmplitude, 0, 1);
        }

        /// <summary>
        /// Send the rumble data the whatever controller connected.
        /// </summary>
        /// <param name="rumble"></param>
        private void PlayRumble(SwitchControllerRumbleProfile rumble)
        {
            rumble.PlayOn(SwitchProControllerNewHID.current, SwitchJoyConLHID.current, SwitchJoyConRHID.current);
        }

        /// <summary>
        /// Create a "Play" button for each <see cref="RumbleDataSheet"/> asset currently selected. 
        /// </summary>
        /// <param name="fold">Is the panel folded or not</param>
        /// <returns></returns>
        private bool RumbleDataSheetPlayer(bool fold)
        {
            // Grab the RumbleDataSheet assets from the selection
            Object[] selection = Selection.GetFiltered(typeof(RumbleDataSheet), SelectionMode.Assets);

            // If nothing is select, don't bother displaying the fold
            if(selection.Length == 0)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label("Rumble data sheet player: ", EditorStyles.boldLabel);
                GUILayout.Label("(none selected)");
                GUILayout.EndHorizontal();
                return fold;
            }

            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Rumble data sheet player: ");
            if (fold)
            {
                // Each select file matches with a little play button to test the sequences of vibrations and stuff
                foreach (Object asset in selection)
                {
                    RumbleDataSheet rumbleAsset = asset as RumbleDataSheet;
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label(asset.name + ": ");

                    //! Sending a single rumble profile makes it slowly fades over roughly 1s, which I find not great
                    // TODO: Craft a dynamic sized array for button values to make the vibrations snappy
                    if (GUILayout.Button("Play"))
                    {
                        PlayRumble(rumbleAsset.profile);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            return fold;
        }
        
        /// <summary>
        /// Execute roughly 10 times/s.
        /// </summary>
        private void OnInspectorUpdate()
        {
            // We have to do that in the InspectorUpdate or the irregular refresh rate messes with the vibrations
            if (playProfileToggle)
            {
                PlayRumble(rumbleProfile);
            }
            else if (playProfileToggledLastFrame)
            {
                PlayRumble(SwitchControllerRumbleProfile.CreateNeutral());
                playProfileToggledLastFrame = false;
            }
        }
    }
}
