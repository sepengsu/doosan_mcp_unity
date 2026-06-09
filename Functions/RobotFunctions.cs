using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mcp.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Mcp.Functions
{
    public static class RobotFunctions
    {
        private static readonly string[] ArmJointNames =
        {
            "joint_1", "joint_2", "joint_3", "joint_4", "joint_5", "joint_6"
        };

        private static Coroutine activeJogRoutine;

        public static Task<object> Handle(JObject parameters)
        {
            return MainThreadDispatcher.Run(() =>
            {
                string action = parameters["action"]?.ToString()?.ToLowerInvariant();
                return action switch
                {
                    "set_arm_joints" => SetArmJoints(parameters),
                    "jog_arm" => JogArm(parameters),
                    "jogging" => Jogging(parameters),
                    "stop_jog" => StopJog(),
                    "set_gripper" => SetGripper(parameters),
                    "get_robot_state" => GetRobotState(),
                    _ => throw new InvalidOperationException($"Unknown robot action '{action}'.")
                };
            });
        }

        private static object SetArmJoints(JObject parameters)
        {
            RosSubscribeArmJoints subscriber = UnityEngine.Object.FindObjectOfType<RosSubscribeArmJoints>();
            if (subscriber == null)
            {
                throw new InvalidOperationException("RosSubscribeArmJoints was not found in the scene.");
            }

            double[] values = ReadJointArray(parameters);
            for (int i = 0; i < ArmJointNames.Length; i++)
            {
                subscriber.jointPositionMap[ArmJointNames[i]] = (float)values[i];
            }

            return new
            {
                message = "A0509 arm joint preview updated.",
                joint_names = ArmJointNames,
                positions_rad = values
            };
        }

        private static object JogArm(JObject parameters)
        {
            RosJogPublisher publisher = UnityEngine.Object.FindObjectOfType<RosJogPublisher>();
            if (publisher == null)
            {
                throw new InvalidOperationException("RosJogPublisher was not found in the scene.");
            }

            string command = parameters["command"]?.ToString();
            if (string.IsNullOrWhiteSpace(command) ||
                !Enum.TryParse(command, true, out RosJogPublisher.JogCommand jogCommand))
            {
                throw new InvalidOperationException("Missing or invalid 'command' for jog_arm.");
            }

            publisher.PublishJog(jogCommand);
            return new { message = $"Published A0509 jog command '{jogCommand}'." };
        }

        private static object Jogging(JObject parameters)
        {
            RosJogPublisher publisher = UnityEngine.Object.FindObjectOfType<RosJogPublisher>();
            if (publisher == null)
            {
                throw new InvalidOperationException("RosJogPublisher was not found in the scene.");
            }

            RosJogPublisher.JogCommand jogCommand = ReadJogCommand(parameters);
            float durationSeconds = Mathf.Max(0.02f, (float)ReadDouble(parameters, "duration_seconds", 1.0d));
            float repeatHz = Mathf.Max(1f, (float)ReadDouble(parameters, "repeat_hz", 10.0d));

            if (activeJogRoutine != null)
            {
                publisher.StopCoroutine(activeJogRoutine);
                publisher.PublishStop();
            }

            activeJogRoutine = publisher.StartCoroutine(JogForDuration(publisher, jogCommand, durationSeconds, repeatHz));
            return new
            {
                message = $"Started timed A0509 jogging '{jogCommand}'.",
                command = jogCommand.ToString(),
                duration_seconds = durationSeconds,
                repeat_hz = repeatHz
            };
        }

        private static object StopJog()
        {
            RosJogPublisher publisher = UnityEngine.Object.FindObjectOfType<RosJogPublisher>();
            if (publisher == null)
            {
                throw new InvalidOperationException("RosJogPublisher was not found in the scene.");
            }

            if (activeJogRoutine != null)
            {
                publisher.StopCoroutine(activeJogRoutine);
                activeJogRoutine = null;
            }

            publisher.PublishStop();
            return new { message = "Published A0509 jog stop." };
        }

        private static object SetGripper(JObject parameters)
        {
            Ag145RosJointSubscriber subscriber = UnityEngine.Object.FindObjectOfType<Ag145RosJointSubscriber>();
            if (subscriber == null)
            {
                throw new InvalidOperationException("Ag145RosJointSubscriber was not found in the scene.");
            }

            double position = ReadDouble(parameters, "position_rad", double.NaN);
            if (double.IsNaN(position))
            {
                double openPercent = ReadDouble(parameters, "open_percent", double.NaN);
                if (double.IsNaN(openPercent))
                {
                    throw new InvalidOperationException("set_gripper needs 'position_rad' or 'open_percent'.");
                }

                openPercent = Math.Max(0d, Math.Min(100d, openPercent));
                position = 0.93d - (openPercent / 100d) * 0.93d;
            }

            subscriber.ApplyMcpGripperPosition(position);
            return new { message = "AG145 gripper preview updated.", position_rad = position };
        }

        private static object GetRobotState()
        {
            RosSubscribeArmJoints arm = UnityEngine.Object.FindObjectOfType<RosSubscribeArmJoints>();
            Ag145RosJointSubscriber gripper = UnityEngine.Object.FindObjectOfType<Ag145RosJointSubscriber>();

            Dictionary<string, float> armState = arm == null
                ? new Dictionary<string, float>()
                : ArmJointNames.ToDictionary(name => name, name => arm.jointPositionMap.TryGetValue(name, out float v) ? v : 0f);

            return new
            {
                a0509 = armState,
                has_arm_subscriber = arm != null,
                has_gripper_subscriber = gripper != null
            };
        }

        private static double[] ReadJointArray(JObject parameters)
        {
            JToken token = parameters["positions_rad"] ?? parameters["positions"];
            if (token is not JArray array || array.Count != ArmJointNames.Length)
            {
                throw new InvalidOperationException("set_arm_joints needs 'positions_rad' as an array of 6 radians.");
            }

            return array.Select(item => item.ToObject<double>()).ToArray();
        }

        private static double ReadDouble(JObject parameters, string key, double fallback)
        {
            JToken token = parameters[key];
            return token == null ? fallback : token.ToObject<double>();
        }

        private static RosJogPublisher.JogCommand ReadJogCommand(JObject parameters)
        {
            string command = parameters["command"]?.ToString();
            if (!string.IsNullOrWhiteSpace(command) &&
                Enum.TryParse(command, true, out RosJogPublisher.JogCommand parsed))
            {
                return parsed;
            }

            string axis = parameters["axis"]?.ToString()?.Trim().ToLowerInvariant();
            int direction = Math.Sign(ReadDouble(parameters, "direction", 1d));
            if (direction == 0)
            {
                direction = 1;
            }

            return axis switch
            {
                "x" => direction > 0 ? RosJogPublisher.JogCommand.XPlus : RosJogPublisher.JogCommand.XMinus,
                "y" => direction > 0 ? RosJogPublisher.JogCommand.YPlus : RosJogPublisher.JogCommand.YMinus,
                "z" => direction > 0 ? RosJogPublisher.JogCommand.ZPlus : RosJogPublisher.JogCommand.ZMinus,
                "rx" => direction > 0 ? RosJogPublisher.JogCommand.RXPlus : RosJogPublisher.JogCommand.RXMinus,
                "ry" => direction > 0 ? RosJogPublisher.JogCommand.RYPlus : RosJogPublisher.JogCommand.RYMinus,
                "rz" => direction > 0 ? RosJogPublisher.JogCommand.RZPlus : RosJogPublisher.JogCommand.RZMinus,
                _ => throw new InvalidOperationException("jogging needs 'command' or 'axis' as x, y, z, rx, ry, or rz.")
            };
        }

        private static IEnumerator JogForDuration(
            RosJogPublisher publisher,
            RosJogPublisher.JogCommand command,
            float durationSeconds,
            float repeatHz)
        {
            float deadline = Time.time + durationSeconds;
            WaitForSeconds wait = new WaitForSeconds(1f / repeatHz);

            while (Time.time < deadline)
            {
                publisher.PublishJog(command);
                yield return wait;
            }

            publisher.PublishStop();
            activeJogRoutine = null;
        }
    }
}
