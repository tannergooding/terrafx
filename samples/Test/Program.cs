// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX.ApplicationModel;
using TerraFX.UI;

namespace TerraFX.Samples
{
    public static unsafe class Program
    {
        internal static readonly Assembly s_entryAssembly = Assembly.GetEntryAssembly()!;
        internal static readonly string s_assemblyPath = Path.GetDirectoryName(s_entryAssembly.Location)!;
        internal static readonly Assembly s_uiProviderWin32 = Assembly.LoadFrom(Path.Combine(s_assemblyPath, "TerraFX.UI.Providers.Win32.dll"));
        internal static readonly Assembly s_uiProviderXlib = Assembly.LoadFrom(Path.Combine(s_assemblyPath, "TerraFX.UI.Providers.Xlib.dll"));

        private static readonly ConcurrentQueue<Action> s_work = new ConcurrentQueue<Action>();
        private static Window s_window = null!;
        private static Application s_application = null!;
        private static WindowProvider s_windowProvider = null!;

        public static void RunThread()
        {
            while (true)
            {
                var line = Console.ReadLine()!;
            
                var parts = line.Split(' ');
            
                switch (parts[0])
                {
                    case "activate":
                    {
                        s_work.Enqueue(() => s_window.Activate());
                        break;
                    }

                    case "close":
                    {
                        s_work.Enqueue(() => s_window.Close());
                        break;
                    }
                    
                    case "create":
                    {
                        s_work.Enqueue(() => s_window = s_windowProvider.CreateWindow());
                        break;
                    }
            
                    case "disable":
                    {
                        s_work.Enqueue(() => s_window.Disable());
                        break;
                    }
            
                    case "dispose":
                    {
                        s_work.Enqueue(() => {
                            s_window.Dispose();
                            s_window = null!;
                        });
                        break;
                    }
            
                    case "enable":
                    {
                        s_work.Enqueue(() => s_window.Enable());
                        break;
                    }
                
                    case "hide":
                    {
                        s_work.Enqueue(() => s_window.Hide());
                        break;
                    }
                
                    case "get_bounds":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.Bounds));
                        break;
                    }
                
                    case "get_client_bounds":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.ClientBounds));
                        break;
                    }
                
                    case "get_is_active":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.IsActive));
                        break;
                    }
                
                    case "get_is_enabled":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.IsEnabled));
                        break;
                    }

                
                    case "get_is_visible":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.IsVisible));
                        break;
                    }
                                    
                    case "get_title":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.Title));
                        break;
                    }

                    case "get_window_state":
                    {
                        s_work.Enqueue(() => Console.WriteLine(s_window.WindowState));
                        break;
                    }

                    case "maximize":
                    {
                        s_work.Enqueue(() => s_window.Maximize());
                        break;
                    }
                
                    case "minimize":
                    {
                        s_work.Enqueue(() => s_window.Minimize());
                        break;
                    }
                
                    case "relocate":
                    {
                        s_work.Enqueue(() => s_window.Relocate(new Numerics.Vector2(float.Parse(parts[1]), float.Parse(parts[2]))));
                        break;
                    }
                    
                    case "relocate_client":
                    {
                        s_work.Enqueue(() => s_window.RelocateClient(new Numerics.Vector2(float.Parse(parts[1]), float.Parse(parts[2]))));
                        break;
                    }
                
                    case "resize":
                    {
                        s_work.Enqueue(() => s_window.Resize(new Numerics.Vector2(float.Parse(parts[1]), float.Parse(parts[2]))));
                        break;
                    }
                    
                    case "resize_client":
                    {
                        s_work.Enqueue(() => s_window.ResizeClient(new Numerics.Vector2(float.Parse(parts[1]), float.Parse(parts[2]))));
                        break;
                    }
                
                    case "restore":
                    {
                        s_work.Enqueue(() => s_window.Restore());
                        break;
                    }
                
                    case "set_title":
                    {
                        s_work.Enqueue(() => s_window.SetTitle(parts[1]));
                        break;
                    }
                
                    case "show":
                    {
                        s_work.Enqueue(() => s_window.Show());
                        break;
                    }
                    
                    default:
                    {
                        Console.WriteLine("unrecognized command");
                        break;
                    }
                }
            }
        }

        public static void Main(string[] args)
        {
            Console.WriteLine(Process.GetCurrentProcess().Id);
        
            var thread = new Thread(RunThread);
            thread.Start();
        
            s_application = new Application(new Assembly[] { RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? s_uiProviderWin32 : s_uiProviderXlib });
            s_windowProvider = s_application.GetService<WindowProvider>();
            
            s_application.Idle += OnIdle;
            s_application.Run();
            s_application.Dispose();
        }
        
        public static void OnIdle(object? sender, ApplicationIdleEventArgs eventArgs)
        {
            if (s_work.TryDequeue(out var work))
            {
                work();
            }
        }
    }
}
