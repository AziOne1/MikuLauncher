using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MikuLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "MikuLauncher";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                if ((bool) MikuLauncher.Properties.Settings.Default["restarting"] == false)
                {
                    MessageBox.Show("始めた / Приложение уже запущено");
                    Application.Current.Shutdown();
                }
                else
                {
                    MikuLauncher.Properties.Settings.Default["restarting"] = false;
                    MikuLauncher.Properties.Settings.Default.Save();
                    MikuLauncher.Properties.Settings.Default.Reload();
                }
            }

            base.OnStartup(e);
        }
    }


    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //timer.Elapsed += OnTimedEvent;
            // wbc.DownloadProgressChanged += wbc_progress;
            // wbc.DownloadFileCompleted += wbc_completed;


            worker = new Thread(launcher_work);
            worker.Start();
        }

        private Thread worker;
        private bool logged_in = false;
        private bool check_finished = false;

        private void launcher_work()
        {
            //return;
            Launcher miku = new Launcher();
            miku.timeout = 10000;
            miku.download_retry = 6;
            miku.show_progress_event += (int progress) =>
            {
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    pb_download.Value = progress;
                }));
                //pb_download_miku.Value= e.ProgressPercentage;
                Debug.WriteLine(progress);
            };
            miku.set_progress_max_event += (int max) =>
            {
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    pb_download_miku.Maximum = max;
                }));
                //pb_download_miku.Value= e.ProgressPercentage;
                //Debug.WriteLine(progress);
            };
            miku.set_progress_miku_event += (int progress, string text) =>
            {
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    pb_download_miku.Value = progress;
                    t_loading.Text = text;
                }));

            };

            int update_needed= miku.check_launcher_updates();
            if (update_needed == -1)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    MessageBox.Show("Не удалось проверить обновления лаунчера. Проверьте подключение к интернету. (0)");
                    exit_app();
                }));
                return;
            }else if (update_needed == 1)
            {
                try
                {
                    miku.launcher_update();
                    this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                    {
                        System.Windows.Application.Current.Shutdown();
                    }));
                    return;
                }
                catch (Exception e)
                {
                    this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate()
                    {
                        MessageBox.Show("Не удалось скачать обновления лаунчера.(1)");
                    }));
                    //return;
                }
            }
            else if (update_needed == 0)
            {
                miku.log_error("Got latest version: "+ Launcher.version);
            }

            int code;
            string s=miku.get_news(out code);
            if (code == 0)
            {
                //do nothing
            }
            else if (code == 1)
            {
                //show and continue
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    MessageBox.Show(s.Substring(1, s.Length - 1));
                }));
            }
            else if (code == 2)
            {
                //show and shutdown
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    MessageBox.Show(s.Substring(1, s.Length - 1));
                    exit_app();
                }));
                return;
            }

            this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
            {
                pb_download.Value = pb_download.Maximum;
                t_loading.Text = "点検 / Проверка";
            }));

            try
            {
                if ((bool) Properties.Settings.Default["check_faster"])
                {                    
                    miku.check_files_fast();
                }
                else
                {
                    miku.check_files();
                    Properties.Settings.Default["check_faster"] = true;
                }
            }
            catch (Exception e)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
                {
                    pb_download.Value = pb_download.Maximum;
                    t_loading.Text = "エラー / Ошибка";
                    if (e.InnerException == null)
                        MessageBox.Show("Не удалось получить список файлов. Похоже, сервер сейчас не работает.\n\n" + e.Message);
                    else
                        MessageBox.Show("Не удалось получить список файлов. Похоже, сервер сейчас не работает.\n\n" + e.InnerException.Message);
                    exit_app();
                }));
            }

            try
            {
                miku.swap_wallpapers();
            }
            catch (Exception e)
            {
                if (e.InnerException == null)
                    miku.log_error("Похоже, у вас установлена очень старая версия клиента. А, может, просто не хватает нескольких файлов. Лучше всего нажать на кнопку \"Починить\" прямо сейчас.\n\n" + e.Message);
                else
                    miku.log_error("Похоже, у вас установлена очень старая версия клиента. А, может, просто не хватает нескольких файлов. Лучше всего нажать на кнопку \"Починить\" прямо сейчас.\n\n" + e.InnerException.Message);
            }


            this.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate ()
            {
                pb_download_miku.Maximum = 100;
                pb_download_miku.Value = 100;
                pb_download.Value = pb_download.Maximum;
                t_loading.Text = "完成した / Завершено";
                t_regtabs.IsEnabled = true;
                check_finished = true;
                if(!logged_in)
                    default_login();
                if(logged_in && check_finished)
                    b_play.IsEnabled = true;                
            }));

            return;
        }

        private void exit_app()
        {
            worker.Abort();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            System.Windows.Application.Current.Shutdown();
        }

        private void b_exit_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
            exit_app();
        }

        private void b_minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void main_window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void fixme(bool go_fast)
        {
            if (go_fast)
                Properties.Settings.Default["check_faster"] = true;
            else
                Properties.Settings.Default["check_faster"] = false;

            Properties.Settings.Default["downloaded"] = false;
            Properties.Settings.Default["restarting"] = true;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
            System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
            exit_app();
        }

        private void b_fix_Click(object sender, RoutedEventArgs e)
        {
            fixme(false);
        }
        private void b_fix_fast_Click(object sender, RoutedEventArgs e)
        {
            fixme(true);
        }
        private void b_play_Click(object sender, RoutedEventArgs e)
        {
            if (!logged_in)
            {
                MessageBox.Show("Войдите / зарегистрируйтесь перед тем как играть.");
                return;
            }
            if (File.Exists("Client\\0101"))
            {
                if (!File.Exists("Client\\game.bin"))
                {
                    File.Copy("Client\\0101", "Client\\game.bin");
                }                
            }
            else
            {
                File.Copy("Client\\game.bin", "Client\\0101");
            }

            if (File.Exists("Client\\game.bin"))
            {
                Process.Start(new ProcessStartInfo("Client\\game.bin")
                {
                    Arguments = "EasyFun -a " + Properties.Settings.Default["last_login"] +" -p "+ Launcher.CalculateMD5Hash((string)Properties.Settings.Default["last_password"]).ToLower(),
                    WorkingDirectory = "Client\\",
                    UseShellExecute = false
                });
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show("Не найден запускаемый файл игры. Антивирус постарался? Начинаю быструю проверку файлов.");
                fixme(true);
            }

        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Closing -= Window_Closing;
            e.Cancel = true;
            var anim = new DoubleAnimation(0, (Duration) TimeSpan.FromSeconds(1));
            anim.Completed += (s, _) => this.Close();
            this.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void b_sign_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool l=Launcher.check_login(t_login.Text, t_password.Text);
                if (l == false)
                {
                    MessageBox.Show("Неправильный логин/пароль.");
                    return;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show("Похоже, войти не получилось. \nИнформация для службы поддержки:\n\n "+ exception.Message);
                return;
            }
            logged_in = true;
            t_regtabs.SelectedItem = t_readytab;
            Properties.Settings.Default["last_login"] = t_login.Text;
            l_accname.Content = t_login.Text;

            Style s = new Style();
            s.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            t_regtabs.ItemContainerStyle = s;

            Properties.Settings.Default["last_password"] = t_password.Text;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
            t_login.Text = "";
            t_password.Text = "";

            if (logged_in && check_finished)
                b_play.IsEnabled = true;
        }

        private void default_login()
        {
            try
            {
                bool l = MikuLauncher.Launcher.default_login();
                if (l == false)
                {
                    //MessageBox.Show("Неправильный логин/пароль.");
                    return;
                }
            }
            catch (Exception exception)
            {
                //MessageBox.Show("Похоже, войти не получилось. \nИнформация для службы поддержки:\n\n " + exception);
                return;
            }
            logged_in = true;
            t_regtabs.SelectedItem = t_readytab;
            l_accname.Content = Properties.Settings.Default["last_login"];

            Style s = new Style();
            s.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            t_regtabs.ItemContainerStyle = s;
            if (logged_in && check_finished)
                b_play.IsEnabled = true;
        }

        private void b_register_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if(t_password_r1.Text==""||t_login_r.Text=="")
                    return;
                
                if (t_password_r1.Text.Trim() != t_password_r2.Text.Trim())
                {
                    MessageBox.Show("Пароль и подтверждение пароля должны быть одинаковы");
                    return;
                }
                bool l = MikuLauncher.Launcher.register(t_login_r.Text, t_password_r1.Text);
                if (l == false)
                {
                    MessageBox.Show("Пользователь с этим логином уже существует.");
                    return;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show("Похоже, регистрация не удалась. \nИнформация для службы поддержки:\n\n " + exception.Message);
                return;
            }
            Properties.Settings.Default["last_login"] = t_login_r.Text;
            Properties.Settings.Default["last_password"] = t_password_r1.Text;
            t_password_r1.Text = "";
            t_login_r.Text = "";
            t_password_r2.Text = "";
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
            default_login();
        }


        private void b_logout_Click(object sender, RoutedEventArgs e)
        {
            logged_in = false;

            Properties.Settings.Default["last_login"] = t_login_r.Text;
            Properties.Settings.Default["last_password"] = t_password_r1.Text;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();

            Style s = new Style();
            s.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            t_regtabs.ItemContainerStyle = s;
            t_regtabs.SelectedItem = t_logintab;
        }

        private void t_login_r_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[0-9]+");
            Regex regex1 = new Regex("[a-z]+");

            e.Handled = !(regex.IsMatch(e.Text) || regex1.IsMatch(e.Text));
        }
        private void textBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Copy ||
                e.Command == ApplicationCommands.Cut ||
                e.Command == ApplicationCommands.Paste)
            {
                e.Handled = true;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }

    }
}
