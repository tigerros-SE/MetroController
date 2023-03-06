using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;
using System;

namespace IngameScript.Services {
	public class PropulsionService {
		/// <summary>
		/// The amount of resistance (average) that the Metro receives. [N]
		/// </summary>
		private const int Resistance = 400;
		/// <summary>
		/// How much the friction will be increased to brake every <see cref="UpdateFrequency.Update10"/> or <see cref="UpdateFrequency.Update1"/>.
		/// </summary>
		private const int FrictionStep = 2;
		/// <summary>
		/// Maximum allowed friction percentage.
		/// </summary>
		private const int MaxFriction = 100;
		/// <summary>
		/// The maximum grid velocity. This is limited by the game. [m/s]
		/// </summary>
		private const int MaxGridVelocity = 100;
		/// <summary>
		/// The maximum velocity that the Metro can go on straight tracks. [m/s]
		/// </summary>
		public const int StraightVelocity = 100;
		/// <summary>
		/// The maximum velocity that the Metro can go in turns. [m/s]
		/// </summary>
		public const int TurnVelocity = 55;
		public const int PreStopVelocity = 10;
		private int _targetVelocity;
		private bool _isVelocityChangePositive;
		private readonly List<IMyThrust> _forwardThrusters;
		private readonly List<IMyMotorSuspension> _suspensions = new List<IMyMotorSuspension>();
		private readonly IMyShipController _cockpit;
		private readonly MyGridProgram _program;

		public PropulsionService(MyGridProgram program) {
			_program = program;
			
			var cockpits = new List<IMyShipController>();

			program.GridTerminalSystem.GetBlocksOfType(cockpits);

			if (cockpits.Count == 0) {
				throw new Exception("No cockpits found.");
			}

			// First check for main cockpit
			for (int i = 0; i < cockpits.Count; i++) {
				if (!cockpits[i].IsMainCockpit) continue;

				_cockpit = cockpits[i];
				break;
			}

			if (_cockpit == null) {
				// Then for first one that can control ship (for example, seats and toilets can't)
				for (int i = 0; i < cockpits.Count; i++) {
					if (!cockpits[i].CanControlShip) continue;

					_cockpit = cockpits[i];
					break;
				}

				if (_cockpit == null) {
					// Then if it's being controlled
					for (int i = 0; i < cockpits.Count; i++) {
						if (!cockpits[i].IsUnderControl) continue;

						_cockpit = cockpits[i];
						break;
					}
					
					if (_cockpit == null) {
						// And if no previous conditions were satisfied, just use the first one
						_cockpit = cockpits[0];
					}
				}
			}

			var thrusters = new List<IMyThrust>();

			program.GridTerminalSystem.GetBlocksOfType(thrusters);
			program.GridTerminalSystem.GetBlocksOfType(_suspensions);

			_forwardThrusters = new List<IMyThrust>(thrusters.Count); // Will be less, but this way the list doesn't need to resize

			Matrix cockpitMatrix;
			_cockpit.Orientation.GetMatrix(out cockpitMatrix);
			
			for (int i = 0; i < thrusters.Count; i++) {
				IMyThrust thruster = thrusters[i];
				
				Matrix thrusterMatrix;
				thruster.Orientation.GetMatrix(out thrusterMatrix);

				if (thrusterMatrix.Forward == cockpitMatrix.Backward) {
					_forwardThrusters.Add(thruster);
				}
			}
		}

		private int GetShipVelocity() => (int)Math.Round(_cockpit.GetShipVelocities().LinearVelocity.Length());

		/// <summary>
		/// Increases the friction of all suspensions by <see cref="FrictionStep"/>.
		/// </summary>
		private void IncreaseFriction() {
			for (int i = 0; i < _suspensions.Count; i++) {
				float increased = _suspensions[i].Friction + FrictionStep;

				if (increased > MaxFriction) {
					_suspensions[i].Friction = MaxFriction;
				} else {
					_suspensions[i].Friction = increased;
				}
			}
		}

		/// <summary>
		/// Sets all thruster overrides to a small amount to counteract resistance (gravity, colliding with guard rails).
		/// </summary>
		private void ThrusterOverrideToIgnoreResistance() {
			for (int i = 0; i < _forwardThrusters.Count; i++) {
				_forwardThrusters[i].ThrustOverride = Resistance / _forwardThrusters.Count;
			}
		}

		/// <summary>
		/// Sets all forward thruster overrides to the max.
		/// </summary>
		private void MaxThrusterOverride() {
			for (int i = 0; i < _forwardThrusters.Count; i++) {
				_forwardThrusters[i].ThrustOverride = _forwardThrusters[i].MaxThrust;
			}
		}

		/// <summary>
		/// Disables the friction of all suspensions.
		/// Doesn't actually turn off the suspensions, but sets the friction to 0.
		/// </summary>
		private void DisableFriction() {
			for (int i = 0; i < _suspensions.Count; i++) {
				_suspensions[i].Friction = 0;
			}
		}

		/// <summary>
		/// Disables the forward thrusters.
		/// Doesn't actually turn off the thrusters, but sets the thruster overrides to 0.
		/// </summary>
		private void DisableThrusters() {
			for (int i = 0; i < _forwardThrusters.Count; i++) {
				_forwardThrusters[i].ThrustOverride = 0;
			}
		}

		/// <summary>
		/// Disables the forward thrusters and the friction of all suspensions.
		/// Doesn't actually turn off the blocks, but sets the thruster overrides to 0 and friction to 0.
		/// </summary>
		private void DisableThrustersAndFriction() {
			DisableThrusters();
			DisableFriction();
		}

		/// <summary>
		/// Call this after every <see cref="UpdateFrequency.Update10"/> if you used the <see cref="ChangeVelocityTo(int)"/> method.
		/// It adjusts the thrusters according to the desired velocity from the previous call to <see cref="ChangeVelocityTo(int)"/> .
		/// </summary>
		public void FinalizeVelocityChange() {
			int shipVelocity = GetShipVelocity();

			if ((_isVelocityChangePositive && (shipVelocity >= _targetVelocity)) ||
			    (!_isVelocityChangePositive && (shipVelocity <= _targetVelocity))) {
				_program.Echo($"Target velocity of {_targetVelocity.ToString()} reached. (Current velocity = {shipVelocity.ToString()})");
				_program.Runtime.UpdateFrequency = UpdateFrequency.None;

				DisableFriction();
				ThrusterOverrideToIgnoreResistance();
			} else if (!_isVelocityChangePositive) {
				IncreaseFriction();
			}
		}

		/// <summary>
		/// Updates the thruster overrides/suspension frictions according to the velocity.
		/// Call this at most every <see cref="UpdateFrequency.Update10"/>.
		/// </summary>
		public void Update() {
			int shipVelocity = GetShipVelocity();

			if (shipVelocity > _targetVelocity) { // Hold your horses!
				DisableThrusters();
				IncreaseFriction();
			} else if (shipVelocity < _targetVelocity) { // Not that much though. Horses are for horsin' around, not standin' around.
				MaxThrusterOverride();
				DisableFriction();
			} else if (shipVelocity == _targetVelocity) { // Just like that. Perfectly balanced.
				ThrusterOverrideToIgnoreResistance();
				DisableFriction();
			}
		}

		/// <summary>
		/// Changes the velocity to the vaue of the <paramref name="targetVelocity"/> argument.
		/// </summary>
		/// <param name="targetVelocity">The new velocity, in meters per second.</param>
		public void ChangeVelocityTo(int targetVelocity) {
			_targetVelocity = targetVelocity;
			
			int shipVelocity = GetShipVelocity();

			if (targetVelocity == shipVelocity) {
				DisableThrustersAndFriction();
			} else if (targetVelocity >= MaxGridVelocity) {
				_program.Echo("Increasing speed to max speed");
				_isVelocityChangePositive = true;

				MaxThrusterOverride();
				DisableFriction();
			} else if (targetVelocity > shipVelocity) {
				_program.Echo($"Increasing speed to {targetVelocity.ToString()}");
				_isVelocityChangePositive = true;

				MaxThrusterOverride();
				DisableFriction();
			} else if (targetVelocity < shipVelocity) {
				_program.Echo($"Decreasing speed to {targetVelocity.ToString()}");
				_isVelocityChangePositive = false;

				DisableThrusters();
				IncreaseFriction();
			}

			_program.Runtime.UpdateFrequency = UpdateFrequency.Update10;
		}
	}
}