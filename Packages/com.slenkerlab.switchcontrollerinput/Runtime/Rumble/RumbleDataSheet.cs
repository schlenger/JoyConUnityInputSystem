namespace UnityEngine.InputSystem.Switch
{
    /// <summary>
    /// ScriptableObject to create reusable vibration profiles through the Editor.
    /// </summary>
    [CreateAssetMenu(fileName = "RumbleData", menuName = "JoyconInput/RumbleDataSheet", order = 1)]
    public class RumbleDataSheet : ScriptableObject
    {
        [SerializeField]
        public SwitchControllerRumbleProfile profile = SwitchControllerRumbleProfile.CreateNeutral();

        /// <summary>
        /// Play the rumble defined by the structure on the specified controller if it exists.
        /// </summary>
        /// <param name="controller"></param>
        public void PlayOn(params SwitchControllerHID[] controllers)
        {
            profile.PlayOn(controllers);
        }
    } 
}
