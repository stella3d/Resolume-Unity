using OscJack;

namespace Resolink
{
    public class FloatOscEventHandler : OscEventHandler<FloatUnityEvent, float>
    {
        protected override float GetMessageValue(OscDataHandle dataHandle)
        {
            return dataHandle.GetElementAsFloat(0);
        }

        public override void SendValue()
        {
            OscRouter.Client.Send(Shortcut.Input.Path, Value);
        }
    }
}