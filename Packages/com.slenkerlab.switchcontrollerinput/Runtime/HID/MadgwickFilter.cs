namespace UnityEngine.InputSystem.Switch.LowLevel
{
    /// <summary>
    /// Madgwick's IMU sensor fusion, estimating an orientation from gyroscope and accelerometer readings.
    /// Implements the IMU variant described in S. Madgwick, "An efficient orientation filter for inertial
    /// and inertial/magnetic sensor arrays" (2010).
    /// </summary>
    internal struct MadgwickFilter
    {
        // The estimated orientation as a quaternion
        private float w, x, y, z;

        public static MadgwickFilter Identity => new MadgwickFilter { w = 1.0f };

        public Quaternion Rotation
        {
            get => new Quaternion(x, y, z, w);
            set
            {
                w = value.w;
                x = value.x;
                y = value.y;
                z = value.z;
            }
        }

        /// <param name="gyro">Angular velocity in radians per second.</param>
        /// <param name="accel">Acceleration, in any unit as only its direction is used.</param>
        /// <param name="beta">Algorithm gain, how much the accelerometer corrects the gyroscope.</param>
        /// <param name="samplePeriod">Time since the previous update in seconds.</param>
        public void Update(Vector3 gyro, Vector3 accel, float beta, float samplePeriod)
        {
            // Rate of change of the orientation as measured by the gyroscope: 1/2 * q * (0, gyro)
            float dw = 0.5f * (-x * gyro.x - y * gyro.y - z * gyro.z);
            float dx = 0.5f * (w * gyro.x + y * gyro.z - z * gyro.y);
            float dy = 0.5f * (w * gyro.y + z * gyro.x - x * gyro.z);
            float dz = 0.5f * (w * gyro.z + x * gyro.y - y * gyro.x);

            // Without a direction of gravity there is nothing to correct the gyroscope with
            if (accel.sqrMagnitude > 0.0f)
            {
                Vector3 measuredGravity = accel.normalized;

                // Objective function: the direction of gravity predicted by the orientation minus the measured one
                float fx = 2.0f * (x * z - w * y) - measuredGravity.x;
                float fy = 2.0f * (w * x + y * z) - measuredGravity.y;
                float fz = 2.0f * (0.5f - x * x - y * y) - measuredGravity.z;

                // Its gradient, the transposed Jacobian applied to the objective function
                float gradW = -2.0f * y * fx + 2.0f * x * fy;
                float gradX = 2.0f * z * fx + 2.0f * w * fy - 4.0f * x * fz;
                float gradY = -2.0f * w * fx + 2.0f * z * fy - 4.0f * y * fz;
                float gradZ = 2.0f * x * fx + 2.0f * y * fy;

                // Only the direction of the gradient is used. It has none when the prediction is already exact.
                float gradMagnitude = Mathf.Sqrt(gradW * gradW + gradX * gradX + gradY * gradY + gradZ * gradZ);
                if (gradMagnitude > 0.0f)
                {
                    float gain = beta / gradMagnitude;
                    dw -= gain * gradW;
                    dx -= gain * gradX;
                    dy -= gain * gradY;
                    dz -= gain * gradZ;
                }
            }

            w += dw * samplePeriod;
            x += dx * samplePeriod;
            y += dy * samplePeriod;
            z += dz * samplePeriod;

            float magnitude = Mathf.Sqrt(w * w + x * x + y * y + z * z);
            w /= magnitude;
            x /= magnitude;
            y /= magnitude;
            z /= magnitude;
        }
    }
}
