using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Switch;

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerInput pInput = null;

    [SerializeField] Vector2 m_movement = new Vector2();

    // Update is called once per frame
    void Update()
    {
        transform.Translate(new Vector3(m_movement.x, 0, m_movement.y));
    }

    void OnFire()
    {
        Debug.Log("doing command");
        StartCoroutine(QuickRumble());
    }

    IEnumerator QuickRumble()
    {
        SwitchControllerHID controller = SwitchControllerHID.current;
        if (controller == null)
            yield break;

        controller.Rumble(new SwitchControllerRumbleProfile
        {
            lowBandFrequencyLeft = 0,
            lowBandAmplitudeLeft = 0,
            
            lowBandFrequencyRight = 0,
            lowBandAmplitudeRight = 0,
            
            highBandFrequencyRight = 150,
            highBandAmplitudeRight = 1
        });
        yield return new WaitForSeconds(0.1f);
        controller.Rumble(new SwitchControllerRumbleProfile
        {
            lowBandFrequencyLeft = 0,
            lowBandAmplitudeLeft = 0,
            
            lowBandFrequencyRight = 0,
            lowBandAmplitudeRight = 0,
            
            highBandFrequencyRight = 115,
            highBandAmplitudeRight = 0
        });
    }
}
