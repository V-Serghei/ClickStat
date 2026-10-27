using System.Windows.Forms;
using System.Windows.Threading;
using ClickStat.Presentation.Services;

namespace ClickStat.Tests;

public class LiveEventBusTests
{
    [Theory]
    [InlineData((int)MouseButtons.Left)]
    [InlineData((int)MouseButtons.Right)]
    [InlineData((int)MouseButtons.Middle)]
    [InlineData((int)MouseButtons.XButton1)]
    [InlineData((int)MouseButtons.XButton2)]
    [InlineData(0)] // Scroll uses the same UI delivery path.
    public async Task MouseFeedback_IsDeliveredDuringContinuousInput(int button)
    {
        var completion = new TaskCompletionSource<(int inputSteps, int received)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                var bus = new LiveEventBus(dispatcher);
                int steps = 0;
                void Receive(int value)
                {
                    completion.TrySetResult((steps, value));
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
                bus.MouseButtonPressed += Receive;
                bus.MouseScrolled += Receive;

                void InputStep()
                {
                    if (++steps < 200)
                        dispatcher.BeginInvoke((Action)InputStep, DispatcherPriority.Input);
                }

                dispatcher.BeginInvoke((Action)InputStep, DispatcherPriority.Input);
                // Publish from a worker, as the real mouse monitor does, before pumping UI.
                Task.Run(() =>
                {
                    if (button == 0) bus.PublishMouseScroll(1);
                    else bus.PublishMouseButton(button);
                }).GetAwaiter().GetResult();
                Dispatcher.Run();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "UI thread did not stop.");
        Assert.Equal(button == 0 ? 1 : button, result.received);
        Assert.InRange(result.inputSteps, 0, 2);
    }
}
