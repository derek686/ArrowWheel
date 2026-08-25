using System;
using System.Diagnostics;
using System.Threading;

class StopArrowWheel
{
    const string EXIT_EVENT = "ArrowWheel_ExitEvent";
    const string PROCESS_NAME = "ArrowWheel";

    static void Main()
    {
        EventWaitHandle[] ewh;
        bool created;

        try
        {
            ewh = new EventWaitHandle[] { new EventWaitHandle(false, EventResetMode.AutoReset, EXIT_EVENT, out created) };
        }
        catch (Exception ex)
        {
            Console.WriteLine("无法访问退出事件: " + ex.Message);
            Pause();
            return;
        }

        ewh[0].Set();

        bool running = false;
        foreach (Process p in Process.GetProcessesByName(PROCESS_NAME))
        {
            running = true;
            p.WaitForExit(2000);
            if (!p.HasExited)
                p.Kill();
        }

        if (running)
            Console.WriteLine("ArrowWheel 已退出。");
        else
            Console.WriteLine("ArrowWheel 未在运行。");

        Pause();
    }

    static void Pause()
    {
        try
        {
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
        catch (InvalidOperationException)
        {
        }
    }
}
