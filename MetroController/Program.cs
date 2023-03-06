using Sandbox.ModAPI.Ingame;
using IngameScript.Services;

namespace IngameScript
{
#pragma warning disable PartialTypeWithSinglePart
    partial class Program : MyGridProgram
    {
        private readonly PropulsionService _propulsionService;

        public Program()
        {
            _propulsionService = new PropulsionService(this);
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (updateSource == UpdateType.Update10)
            {
                _propulsionService.Update();
                return;
            }

            switch (argument)
            {
                case "speed turn":
                    _propulsionService.ChangeVelocityTo(PropulsionService.TurnVelocity);
                    break;
                case "speed straight":
                    _propulsionService.ChangeVelocityTo(PropulsionService.StraightVelocity);
                    break;
                case "speed prestop":
                    _propulsionService.ChangeVelocityTo(PropulsionService.PreStopVelocity);
                    break;
                case "speed stop":
                    _propulsionService.ChangeVelocityTo(0);
                    break;
            }
        }
    }
}