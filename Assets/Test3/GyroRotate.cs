using UnityEngine;
using UnityEngine.InputSystem.Switch;

public class GyroRotate : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        SwitchControllerHID controller = SwitchControllerHID.current;
        if (controller == null)
            return;

        if (controller.buttonSouth.wasReleasedThisFrame)
        {
            controller.ReadUserIMUCalibrationData();
        }
        if (controller.buttonEast.wasReleasedThisFrame)
        {
            controller.ResetYaw();
        }
        transform.localRotation = controller.deviceRotation.ReadValue();
    }
}
