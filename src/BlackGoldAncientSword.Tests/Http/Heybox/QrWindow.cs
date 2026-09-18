using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    internal sealed class QrWindow : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Window _window;

        private QrWindow(Dispatcher dispatcher, Window window)
        {
            _dispatcher = dispatcher;
            _window = window;
        }

        public static QrWindow? Show(byte[] imageBytes, string title)
        {
            QrWindow? created = null;
            using var ready = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(imageBytes);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var window = new Window
                    {
                        Title = title,
                        Width = 520,
                        Height = 580,
                        Content = new Image { Source = bitmap, Stretch = Stretch.Uniform },
                    };

                    created = new QrWindow(Dispatcher.CurrentDispatcher, window);
                    window.Show();
                }
                catch (Exception)
                {
                    created = null;
                }
                finally
                {
                    ready.Set();
                }

                if (created is not null) Dispatcher.Run();
            })
            {
                IsBackground = true,
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            ready.Wait(TimeSpan.FromSeconds(15));
            return created;
        }

        public void Close()
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    _window.Close();
                    _dispatcher.InvokeShutdown();
                });
            }
            catch (Exception)
            {
            }
        }

        public void Dispose() => Close();
    }
}
