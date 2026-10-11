# Nintendo Switch Controllers for Unity Input System

> [!NOTE]
> This is a fork of [Meorge/JoyConUnityInputSystem](https://github.com/Meorge/JoyConUnityInputSystem), which is no longer maintained and has been set to read-only by its author.
> This fork builds on it with fixes for stick drift, third party controllers and newer versions of the Input System package, see [Changes in this fork](#changes-in-this-fork).

This is a prototype for Nintendo Switch controller Bluetooth support for the new Unity Input System, brought to compatibility with the latest versions of Unity and its InputSystem package.

## Installation

Using the Unity Package Manager, add the package from the following git URL: <https://github.com/schlenger/JoyConUnityInputSystem.git?path=/Packages/com.slenkerlab.switchcontrollerinput>.

The package requires Unity 6 (6000.0) or newer and version 1.20.0 or newer of the Input System package.

## Changes in this fork

- **Stick calibration**
  - Stick axes are normalized around the calibrated center instead of the middle of the min/max range, so a resting stick reads exactly 0.
  - When a controller connects, its sticks are assumed to be untouched. If they rest outside of the calibrated center, the rest position is used as the center instead.
  - `SwitchControllerHID.RecenterSticks()` uses the current stick position as the new center, e.g. for a "recalibrate" option in your game.
  - Unusable calibration data (blank flash, as seen on third party controllers) is ignored in favor of the default values.
  - The uncalibrated 12 bit stick values are available as `SwitchControllerHID.RawLeftStick` and `RawRightStick`.
- **Third party controllers**
  - The controller type is taken from the matched layout (i.e. the product ID) if the controller reports a different one about itself.
  - Controllers that only return the body and button colors are supported.
- **Pro Controller**: matched by vendor and product ID regardless of the product name, which differs per platform (e.g. "Pro Controller" on macOS). This also makes the layout take precedence over the Input System's built-in `SwitchProControllerHID`.
- **Input System 1.20**: fixed the layouts being registered before the Input System is initialized in the editor.
- **IMU**
  - The IMU is enabled and its calibration is read when a controller connects. The user calibration is used if the controller has one, otherwise the factory calibration.
  - The orientation is integrated with the time between the controller's reports instead of a fixed time step.
  - Writing a user calibration no longer stores unusable values on the controller.
- **Battery**: the battery level and charging state stay up to date while the controller is connected.
- **Rumble**: frequencies are clamped to the range the controller supports, see the constants on `SwitchControllerRumbleProfile`.
- **Player builds**: the package compiles for standalone players.
- **Logging**: the package only logs warnings and errors, unless `SwitchControllerHID.VerboseLogging` is set to `true`.

## Notes

- Keep in mind that the left and right Joy-Cons are treated by Unity as separate controllers. As a result, you'll need to make sure you use the correct controller in bindings.
  - For example, the control `buttonSouth` exists for "Joy-Con (L)", but it is never used. Whenever you want to refer to the player pressing the "A" button, make sure the device is either "Joy-Con (R)" or "Pro Controller".
- The Input Action editor GUI won't display the controllers when attempting to add them to a control scheme. The only workaround I found for this so far was to go into the `.inputactions` file and add them manually.
- I've experienced a few strange but not (yet) reproducible issues, on occasion:
  - Even though a Joy-Con is connected, no output will appear from it in the Input Debugger. To fix this, I typically shut it off and reconnect it.
  - The Unity Editor will freeze up for a few seconds when connecting a Joy-Con.
- Reconnecting a JoyCon already paired to your computer does not work with a 5.0 Bluetooth device. It is a known hardware problem of the JoyCon, that cannot be fixed without changing the device to a 4.x one.

## Supported controllers

- Joy-Con (L)
- Joy-Con (R)
- Pro Controller
- Third party controllers that identify as one of the above

## Supported features

### Device data analysis

General data about the connected Switch controllers can be found in the menu `Windows/Switch Controller Debugger/Joycon Device Data`. This however doesn't include the input data, which can be found instead `Windows/Analysis/Input Debugger` (as well as the input data of every other controller).

### Input

- A, B, X, Y on Joy-Con (R) and Pro Controller as `buttonEast`, `buttonSouth`, `buttonNorth`, and `buttonEast` respectively
- Up, down, left, and right on Joy-Con (L) and Pro Controller as `dpad`
- Triggers and shoulders, including SL and SR for Joy-Cons
- Capture and HOME buttons
- Minus and Plus buttons
- Left and right analog sticks and stick presses
  - The factory calibration is used, and corrected on connect if the stick rests off center. Leave the sticks untouched while connecting, or call `SwitchControllerHID.RecenterSticks()` afterwards.

### HD Rumble: WIP

High-band and low-band amplitude and frequency for the left and right Joy-Cons (or, in the case of the Pro Controller, the left and right sides of the controller) can be set with the `SwitchControllerHID.Rumble()` method.

Example:

```c#
var rumbleProfile = new SwitchControllerRumbleProfile
    {
        lowBandFrequencyL = 160f,
        lowBandAmplitudeL = 0.1f,
        lowBandFrequencyR = 150f,
        lowBandAmplitudeR = 0.5f,
        
        highBandFrequencyL = 200f,
        highBandAmplitudeL = 0.8f,
        highBandFrequencyR = 150f,
        highBandAmplitudeR = 1.0f
    };
        
controller.Rumble(rumbleProfile);
```

The HD Rumble features can be tested `Windows/Switch Controller Debugger/Joycon Rumble Playground`

### Gyroscope and acceleration (partial)

The controller's IMU is enabled when it connects, and can be turned off and on with `SwitchControllerHID.SetIMUEnabled()`. The `acceleration` and `angularVelocity` controls use the calibration stored on the controller, the raw values are available as `uncalibratedAcceleration` and `uncalibratedAngularVelocity`. `deviceRotation` is the orientation estimated from both.

`SwitchControllerHID.StartIMUCalibration()` (also available in the debug window) stores its result as the user calibration in the controller's flash memory, replacing a user calibration made on the console.

### Player LEDs

The green LEDs on the controllers can be set with `SwitchControllerHID.SetLEDs()`.

Example:

```c#
controller.SetLEDs(
    p1: LEDStatusEnum.Off,
    p2: LEDStatusEnum.On,
    p3: LEDStatusEnum.Flashing,
    p4: LEDStatusEnum.On
    );
```

However, quitting the application doesn't reset the LEDs. If you want them to get back to their initial state, you should either restart the controller or set the LEDs again in the `OnApplicationQuit()` method of a GameObject.

## Credits

- The original [JoyConUnityInputSystem](https://github.com/Meorge/JoyConUnityInputSystem) by [Meorge](https://github.com/Meorge) and its contributors, which this repository is a fork of
- The amazing [Nintendo Switch Reverse Engineering](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering) repository
  - Format for parsing controller data and sending subcommands is from [bluetooth_hid_notes.md](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/bluetooth_hid_notes.md) and [bluetooth_hid_subcommands_notes.md](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/bluetooth_hid_subcommands_notes.md)
  - HD Rumble encoding algorithm is from [rumble_data_table.md](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/rumble_data_table.md)
  - Gyroscope and accelerometer decoding algorithms are from [imu_sensor_notes.md](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/imu_sensor_notes.md)
- [Unity InputSystem fork implementing the Nintendo Switch Pro Controller](https://github.com/Unity-Technologies/InputSystem/pull/1471)
  - The `SwitchControllerFullInputReport.ToHIDInputReport()` method follows the [implementation in SwitchFullInputReport](https://github.com/Unity-Technologies/InputSystem/blob/67a8605dc8d2bb67d251117cbe0e371d043e7a13/Packages/com.unity.inputsystem/InputSystem/Plugins/Switch/SwitchProControllerHID.cs#L360).
  - The `SwitchControllerHID.PreProcessEvent()` method is an expanded version of the [implementation in SwitchProControllerHID](https://github.com/Unity-Technologies/InputSystem/blob/67a8605dc8d2bb67d251117cbe0e371d043e7a13/Packages/com.unity.inputsystem/InputSystem/Plugins/Switch/SwitchProControllerHID.cs#L213).
  - The `SwitchControllerVirtualInputState` class is an expanded version of [SwitchProControllerHIDInputState](https://github.com/Unity-Technologies/InputSystem/blob/67a8605dc8d2bb67d251117cbe0e371d043e7a13/Packages/com.unity.inputsystem/InputSystem/Plugins/Switch/SwitchProControllerHID.cs#L20), with support added for Joy-Cons and IMU data. The format was changed from "SPVS" to "SCVS", since it also supports Joy-Cons.
  - These parts remain subject to the [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license).
- The orientation filter implements the algorithm from Sebastian Madgwick's paper [An efficient orientation filter for inertial and inertial/magnetic sensor arrays](https://x-io.co.uk/downloads/madgwick_internal_report.pdf)

## Disclaimer

This project is not affiliated with, endorsed by or sponsored by Nintendo. Nintendo Switch, Joy-Con and Pro Controller are trademarks of Nintendo, and are only used here to describe which hardware the package is compatible with. The package contains no code, firmware or assets from Nintendo.

The package is provided as is, without warranty of any kind. It sends commands to your controllers and can write to their flash memory, use it at your own risk.
