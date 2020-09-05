using System;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;
using Force.Crc32;
using Ionic.Zlib;
using Timer = System.Timers.Timer;
using System.Linq;
using System.Security.Cryptography;
using Npgsql;

namespace MikuLauncher
{
    public class Launcher
    {
        public ushort timeout { get; set; }

        private const string server = "http://localwebhost/";
        //        private const string server = "http://194.87.237.124/";
        //private const string server = "http://localhost/fakeak/";
        private const string connection_string_1 = @"Server=localhost;Port=5432;Database=ffaccount;Userid=launcheruser;Password=pwd;Timeout=5;CommandTimeout=5;";
        private const string connection_string_2 = @"Server=localhost;Port=5432;Database=ffmember;Userid=launcheruser;Password=pwd;Timeout=5;CommandTimeout=5;";
        public const string version = "1018";
        public int download_retry;
        private string my_crc;

        internal delegate void show_progress(int progress);
        internal event show_progress show_progress_event;

        internal delegate void set_progress_max(int progress);
        internal event set_progress_max set_progress_max_event;

        internal delegate void set_progress_miku(int progress,string text);
        internal event set_progress_miku set_progress_miku_event;

        public Launcher()
        {
            if (File.Exists("ml.log"))
                File.Delete("ml.log");
            if (File.Exists("updater.bat"))
                File.Delete("updater.bat");
            if (File.Exists("GameData_old.idx"))
                File.Delete("GameData_old.idx");
            if (File.Exists("GameData.idx"))
                File.Move("GameData.idx","GameData_old.idx");
        }

        public void swap_wallpapers()
        {
            if (!Directory.Exists("Client\\ui\\loading_images"))
            {
                throw (new Exception("Warning: Directory \"loading_images\" does not exist."));
            }
            string[] files = Directory.GetFiles("Client\\ui\\loading_images");
            if(files.Length<2)
                throw (new Exception("Warning: Not enough \"loading_images\"."));
            Random rnd = new Random();
            int rndnumber = rnd.Next(0,files.Length-1);
            string fn="";
            string fn_hd = "";
            if (files[rndnumber][files[rndnumber].Length - 5] == 'd')
            {
                fn_hd = files[rndnumber];
                fn = files[rndnumber].Substring(0, files[rndnumber].Length - 7) + ".dds";
            }
            else
            {
                fn = files[rndnumber];
                fn_hd = files[rndnumber].Substring(0, files[rndnumber].Length - 4) + "-hd.dds";
            }
            if (File.Exists("Client\\ui\\loadingframe\\loading_000.dds"))
                File.Delete("Client\\ui\\loadingframe\\loading_000.dds");
            if (File.Exists("Client\\ui\\loadingframe\\loading_000-hd.dds"))
                File.Delete("Client\\ui\\loadingframe\\loading_000-hd.dds");
            File.Copy(fn, "Client\\ui\\loadingframe\\loading_000.dds");
            File.Copy(fn_hd, "Client\\ui\\loadingframe\\loading_000-hd.dds");
        }

        public int check_launcher_updates()//0 - allready latest version; 1 - update needed; -1 - error
        {
            try
            {
                download_file_stable(server + "server/version.txt", "v",true);
                if (!File.Exists("v")|| File.ReadAllText("v").Length < 1)
                {
                    return -1;
                }
                else
                {                    
                    string v = File.ReadAllText("v");
                    if (version == v.Split(' ')[0])
                    {
                        return 0;
                    }
                    else
                    {
                        my_crc = v.Split(' ')[1];
                        return 1;
                    }
                }
            }
            catch (Exception e)
            {
                if (e.InnerException == null)
                {
                    log_error("Error (v): " + e.Message+"\n"+e.Data + "\n" +e.HResult + "\n" +e.HelpLink + "\n" +e.Source + "\n" +e.StackTrace + "\n" +e.TargetSite);
                }
                else
                {
                    log_error("Error (v): " + e.Message + "\n" + e.Data + "\n" + e.HResult + "\n" + e.HelpLink + "\n" + e.Source + "\n" + e.StackTrace + "\n" + e.TargetSite + "\n"+ e.InnerException.Message + "\n" +e.InnerException.Data + "\n" +e.InnerException.HResult + "\n" +e.InnerException.HelpLink + "\n" +e.InnerException.Source + "\n" +e.InnerException.StackTrace + "\n" +e.InnerException.HelpLink + "\n");
                }
                return -1;
            }
        }

        void download_file_stable(string furl, string fname,bool show_progress)
        {
            for (int err_ticks = 0; ; err_ticks++)
            {
                Debug.Write(" "+err_ticks);
                try
                {
                    if (show_progress)
                        show_progress_event(Math.Max(100 - err_ticks * 5, 0));
                    var task = download_file_async(furl, fname,show_progress);
                    task.Wait();
                    if (task.IsFaulted || task.IsCanceled||task.Exception!=null)
                        throw task.Exception;
                    return;
                }
                catch (Exception e)
                {
                    if (err_ticks >= download_retry)
                    {
                        throw e;
                    }
                }
            }
        }

        public void launcher_update()
        {
            try
            {
                download_file_stable(server + "server/MikuLauncher.exe", "MikuLauncher_new.exe",true);
                if (!File.Exists("MikuLauncher_new.exe"))
                {
                    throw(new Exception("File not found"));                    
                }
                else
                {
                    byte[] buffer = File.ReadAllBytes("MikuLauncher_new.exe");
                    uint new_crc = Crc32Algorithm.Compute(buffer);
                    uint my_crcu;
                    bool parsedSuccessfully = uint.TryParse(my_crc,
                        NumberStyles.HexNumber,
                        CultureInfo.CurrentCulture,
                        out my_crcu);
                    if (new_crc != my_crcu)
                        throw (new Exception("Can not download launcher update"));

                    do_update();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                log_error("\nError: " + e.Message);
                throw e;
            }

        }

        private void do_update()
        {
            File.WriteAllText("updater.bat", "echo Updating launcher...\n" +
                                             "timeout 5\n" +
                                             "del MikuLauncher.exe\n" +
                                             "ren MikuLauncher_new.exe MikuLauncher.exe\n" +
                                             "start MikuLauncher.exe");
            System.Diagnostics.Process proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = "updater.bat";
            //proc.StartInfo.WorkingDirectory = "C:\\Watcher";
            proc.Start();
            //MessageBox.Show("todo:update");
        }

        private async Task download_file_async(string furl, string fname,bool show_progress)
        {
            //last_fname = fname;
            //last_furl = furl;
            using (WebClient wbc = new WebClient())
            {
                Timer timer = new System.Timers.Timer(timeout);

                timer.Elapsed += (object source, ElapsedEventArgs e) => { wbc.CancelAsync(); };
                //timer.Stop();
                timer.Start();
                wbc.Disposed += new EventHandler(delegate (Object o, EventArgs a)
                {
                    try
                    {
                        if (timer != null)
                        {
                            timer.Stop();
                            timer.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        log_error("Warning: timer disposed after download.");
                    }
                });
                wbc.DownloadProgressChanged += (object sender, DownloadProgressChangedEventArgs e) => {
                    try
                    {
                        if (timer != null)
                        {
                            timer.Stop();
                            timer.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                        log_error("Warning: timer disposed on download.");
                    }
                };
                if (show_progress)
                    wbc.DownloadProgressChanged += wbc_progress;
                //wbc.DownloadFileCompleted += dl_completed;

                await wbc.DownloadFileTaskAsync(new Uri(furl), fname);
            }
        }

        static object locker=new object();
        public void log_error(string err)
        {
            lock (locker)
            {
                using (StreamWriter sw = new StreamWriter("ml.log",true,Encoding.UTF8))
                {
                    sw.WriteLine(err);
                }
            }
        }



        void wbc_progress(object sender, DownloadProgressChangedEventArgs e)
        {
            show_progress_event(e.ProgressPercentage);
            Debug.WriteLine(e.ProgressPercentage);
        }

        public string get_news(out int code)
        {
            try
            {
                download_file_stable(server + "server/launcher.txt", "l", true);
                if (!File.Exists("l"))
                    throw new Exception("File doesn`t exist");
                string a = File.ReadAllText("l");
                code = int.Parse(a.Substring(0, 1));
                return a;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                code = 2;
                return "2Не могу поговорить с сервером. Похоже, он ответил что-то непонятное.\nПроверьте соединение, перезапустите лончер.\nЕсли проблема повторится, обращайтесь в техподдержку.";
            }
        }

        private int get_idx_errs = 0;


        public void check_and_update()
        {

        }

        public void check_files()
        {
            int file_list_changed = 0;
            try
            {
                download_file_stable(server + "GameData.idx", "GameData.idx", true);
                if (!File.Exists("GameData.idx")|| new FileInfo("GameData.idx").Length<=10)
                {
                    throw new Exception("File not exists: GameData.idx");
                }
                show_progress_event(100);
                scan_files_list(out file_list_changed);
            }
            catch (Exception e)
            {
                if (e.InnerException == null)
                    log_error(e.Message);
                else
                    log_error(e.InnerException.Message);
                throw;
            }

            try
            {
                if (file_list_changed==1||file_list_changed==2)
                    check_and_download();
            }
            catch (Exception ex)
            {
                log_error(ex.Message);
                throw;
            }

            Properties.Settings.Default["downloaded"] = true;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();

            if (Directory.Exists("NewData\\"))
            {
                Directory.Delete("NewData\\", true);
            }
            if (File.Exists("l"))
                File.Delete("l");
            if (File.Exists("v"))
                File.Delete("v");
        }

        public void check_files_fast()
        {
            int file_list_changed = 0;
            try
            {
                download_file_stable(server + "GameData.idx", "GameData.idx", true);
                if (!File.Exists("GameData.idx") || new FileInfo("GameData.idx").Length <= 10)
                {
                    throw new Exception("File not exists: GameData.idx");
                }
                show_progress_event(100);
                scan_files_list(out file_list_changed);
            }
            catch (Exception e)
            {
                if (e.InnerException == null)
                    log_error(e.Message);
                else
                    log_error(e.InnerException.Message);
                throw;
            }

            try
            {
                if (file_list_changed==1)
                    check_and_download_fast();
                else if (file_list_changed == 2)
                    check_and_download();
            }
            catch (Exception ex)
            {
                log_error(ex.Message);
                throw;
            }

            Properties.Settings.Default["downloaded"] = true;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();

            if (Directory.Exists("NewData\\"))
            {
                Directory.Delete("NewData\\", true);
            }
            if (File.Exists("l"))
                File.Delete("l");
            if (File.Exists("v"))
                File.Delete("v");



        }

        private int total_files = 0;
        private int processed_files = 0;
        private string[] f_files;
        private int[] f_sizes;
        private uint[] f_crcs;

        private void scan_files_list(out int file_list_changed)
        {
            file_list_changed = 0;
            byte[] buffer = File.ReadAllBytes("GameData.idx");
            uint new_crc = Crc32Algorithm.Compute(buffer);

            if (new_crc != (uint) Properties.Settings.Default["last_idx_crc"] &&(bool) Properties.Settings.Default["downloaded"])
            {
                file_list_changed = 1;
                Properties.Settings.Default["last_idx_crc"] = new_crc;
                Properties.Settings.Default.Save();
                Properties.Settings.Default.Reload();


                BinaryReader binaryReader = new BinaryReader(new FileStream("GameData.idx", FileMode.Open));
                total_files = binaryReader.ReadInt32();

                f_files = new string[total_files];
                f_sizes = new int[total_files];
                f_crcs = new uint[total_files];
                set_progress_max_event(total_files);
                for (long i = 0;
                    binaryReader.BaseStream.Position < binaryReader.BaseStream.Length && i < total_files;
                    ++i)
                {
                    /*if (i > 100)
                    {
                        total_files = 100;
                        break;
                    }*/
                    f_files[i] = binaryReader.ReadString();
                    f_sizes[i] = binaryReader.ReadInt32();
                    f_crcs[i] = binaryReader.ReadUInt32();
                    //string path = UnZip(f_files[i]);
                    //string fileName = System.IO.Path.GetFileName(path);
                    //if (fileName != "C_Item.ini")
                    //{
                    //    f_files[i] = null;
                    //}
                }

                binaryReader.Close();

                if (File.Exists("GameData_old.idx"))
                {
                    file_list_changed = 2;
                    log_error("Found old GameData. Performing an update...");
                    BinaryReader binaryReader2 = new BinaryReader(new FileStream("GameData_old.idx", FileMode.Open));
                    int total_files_old = binaryReader2.ReadInt32();
                    string[] f_files_old = new string[total_files_old];
                    int[] f_sizes_old = new int[total_files_old];
                    uint[] f_crcs_old = new uint[total_files_old];
                    for (long i = 0;
                        binaryReader2.BaseStream.Position < binaryReader2.BaseStream.Length && i < total_files_old;
                        ++i)
                    {
                        f_files_old[i] = binaryReader2.ReadString();
                        f_sizes_old[i] = binaryReader2.ReadInt32();
                        f_crcs_old[i] = binaryReader2.ReadUInt32();
                    }
                    for (int i = 0; i < total_files_old; i++)
                    {
                        int ind = Array.IndexOf(f_files, f_files_old[i]);
                        if (ind > 0)
                        {
                            if (f_sizes[ind] == f_sizes_old[i] && f_crcs[ind] == f_crcs_old[i])
                            {
                                f_files[ind] = null;
                            }
                        }
                    }
                    int updated = f_files.Count(s => s != null);
                    log_error("Got "+updated+" new files. Updating...");
                    set_progress_max_event(updated);
                    binaryReader2.Close();

                }
            }
            else if (new_crc != (uint) Properties.Settings.Default["last_idx_crc"] || !(bool) Properties.Settings.Default["downloaded"])
            {
                file_list_changed = 1;
                Properties.Settings.Default["last_idx_crc"] = new_crc;
                Properties.Settings.Default.Save();
                Properties.Settings.Default.Reload();


                BinaryReader binaryReader = new BinaryReader(new FileStream("GameData.idx", FileMode.Open));
                total_files = binaryReader.ReadInt32();

                f_files = new string[total_files];
                f_sizes = new int[total_files];
                f_crcs = new uint[total_files];
                set_progress_max_event(total_files);
                for (long i = 0;
                    binaryReader.BaseStream.Position < binaryReader.BaseStream.Length && i < total_files;
                    ++i)
                {
                    /*if (i > 100)
                    {
                        total_files = 100;
                        break;
                    }*/
                    f_files[i] = binaryReader.ReadString();
                    f_sizes[i] = binaryReader.ReadInt32();
                    f_crcs[i] = binaryReader.ReadUInt32();
                }

                binaryReader.Close();
            }
        }

        private void check_and_download_fast()
        {
            log_error("Got " + Environment.ProcessorCount + " CPU cores available. Checking(no crc) "+ total_files);

            for (int i = 0; i < total_files; ++i)
            {
                try
                {
                    if (f_files[i] != null)
                        check_single_file_fast(f_files[i], f_sizes[i], f_crcs[i]);
                    set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                }
                catch (Exception e)
                {
                    if (e.InnerException == null)
                        log_error(UnZip(f_files[i]) + " " + i + " " + e.Message);
                    else
                        log_error(UnZip(f_files[i]) + " " + i + " " + e.InnerException.Message);
                }
            }
            /*Parallel.For(0, f_files.Length, (i)=>
            {
                ii++;
                try
                {
                    if (f_files[i] != null)
                        check_single_file_fast(f_files[i], f_sizes[i], f_crcs[i]);
                    else
                        processed_files++;
                    set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                }
                catch (Exception e)
                {
                    if (e.InnerException == null)
                        log_error(UnZip(f_files[i]) + " " + i + " " + e.Message);
                    else
                        log_error(UnZip(f_files[i]) + " " + i + " " + e.InnerException.Message);
                }
            });*/
            
            /* Parallel.ForEach(f_files, (currentFile, state, i) =>
             {
                 try
                 {
                     if (f_files[i] != null)
                         check_single_file_fast(f_files[i], f_sizes[i], f_crcs[i]);
                     set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                 }
                 catch (Exception e)
                 {
                     if (e.InnerException == null)
                         log_error(UnZip(f_files[i]) + " " + i + " " + e.Message);
                     else
                         log_error(UnZip(f_files[i]) + " " + i + " " + e.InnerException.Message);
                 }
             });*/
            log_error(lol);
        }

        private void check_and_download()
        {
            /*            Timer a = new Timer(1000);
                        a.Elapsed += (object source, ElapsedEventArgs e) =>
                        {
                            set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files));
                        };
                        a.Start();*/
            //Thread.Sleep(4000);
            //var options = new ParallelOptions();

            //options.MaxDegreeOfParallelism = Environment.ProcessorCount*4;
            //log_error("Got " + Environment.ProcessorCount + " CPU cores available. Downloading in " + Environment.ProcessorCount * 4 + " threads.");
            log_error("Got " + Environment.ProcessorCount + " CPU cores available. Checking(by crc)  " + total_files);
            /*Parallel.ForEach(f_files,  (currentFile,state,i) =>
            {
                try
                {
                    if(f_files[i]!=null)
                        check_single_file(f_files[i], f_sizes[i], f_crcs[i]);
                    set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                }
                catch (Exception e)
                {
                    if(e.InnerException==null)
                        log_error(UnZip(f_files[i]) + " "+ i +" "+e.Message);
                    else
                        log_error(UnZip(f_files[i]) + " " + i + " " + e.InnerException.Message);
                }
            });*/

            for (int i = 0; i < total_files; ++i)
            {
                 try
                 {
                     if (f_files[i] != null)
                         check_single_file(f_files[i], f_sizes[i], f_crcs[i]);
                     set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                 }
                 catch (Exception e)
                 {
                     if (e.InnerException == null)
                         log_error(UnZip(f_files[i]) + " " + i + " " + e.Message);
                     else
                         log_error(UnZip(f_files[i]) + " " + i + " " + e.InnerException.Message);
                 }
            }
            log_error(lol);

            //var options = new ParallelOptions();
            //
            //options.MaxDegreeOfParallelism = Environment.ProcessorCount*4;
            //Parallel.ForEach(f_files, options, (currentFile,state,i) =>
            //{
            //    try
            //    {
            //        if (f_files[i] != null)
            //        {
            //            string path = UnZip(f_files[i]);
            //            string directoryName = System.IO.Path.GetDirectoryName(path);
            //            string fileName = System.IO.Path.GetFileName(path);
            //            string text = directoryName + "\\_" + fileName;
            //            string url = server + text.Replace("\\", "/");
            //            HttpWebResponse response = null;
            //            var request = (HttpWebRequest)WebRequest.Create(url);
            //            request.Method = "HEAD";
            //            request.Timeout = 5000;
            //            try
            //            {
            //                response = (HttpWebResponse)request.GetResponse();
            //            }
            //            catch (WebException e)
            //            {
            //                /* A WebException will be thrown if the status of the response is not `200 OK` */
            //                if (e.InnerException == null)
            //                    log_error(url + " " + i + " " + e.Message);
            //                else
            //                    log_error(url + " " + i + " " + e.InnerException.Message);
            //            }
            //            finally
            //            {
            //                // Don't forget to close your response.
            //                if (response != null)
            //                {
            //                    response.Close();
            //                }
            //            }
            //        }
            //        processed_files++;
            //        set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
            //    }
            //    catch (Exception e)
            //    {
            //        log_error(e.Message);
            //    }
            //});

            //for (int i = 0; i < total_files; ++i)
            //{
            //    try
            //    {
            //        if (f_files[i] != null)
            //        {
            //            string path = UnZip(f_files[i]);
            //            string directoryName = System.IO.Path.GetDirectoryName(path);
            //            string fileName = System.IO.Path.GetFileName(path);
            //            string text = directoryName + "\\_" + fileName;
            //            string url = server + text.Replace("\\", "/");
            //            HttpWebResponse response = null;
            //            var request = (HttpWebRequest)WebRequest.Create(url);
            //            request.Method = "HEAD";
            //            request.Timeout = 5000;
            //            try
            //            {
            //                response = (HttpWebResponse)request.GetResponse();
            //            }
            //            catch (WebException e)
            //            {
            //                /* A WebException will be thrown if the status of the response is not `200 OK` */
            //                        if(e.InnerException==null)
            //                            log_error(url + " "+ i +" "+e.Message);
            //                        else
            //                            log_error(url + " " + i + " " + e.InnerException.Message);
            //            }
            //            finally
            //            {
            //                // Don't forget to close your response.
            //                if (response != null)
            //                {
            //                    response.Close();
            //                }
            //            }
            //        }
            //        processed_files++;
            //        set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
            //    }
            //    catch (Exception e)
            //    {
            //        log_error(e.Message);
            //    }
            //}

            //            a.Stop();
            /*Parallel.For(0, total_files, (i) =>
            {
                try
                {
                    check_single_file(f_files[i], f_sizes[i], f_crcs[i]);
                    set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                }
                catch (Exception e)
                {
                    log_error(e.Message);
                }
            });*/

            /*for (int i = 0; i < total_files; ++i)
            {
                try
                {
                    if (f_files[i] != null)
                        check_single_file(f_files[i], f_sizes[i], f_crcs[i]);
                    set_progress_miku_event(processed_files, (processed_files + 1) + " / " + (total_files + 1));
                }
                catch (Exception e)
                {
                    log_error(e.Message);
                }
            }*/
        }

        private string lol="";
        private void check_single_file(string file, int size, uint crc)
        {
            string path = UnZip(file);
            string directoryName = System.IO.Path.GetDirectoryName(path);
            string fileName = System.IO.Path.GetFileName(path);
            string str = "NewData\\";
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            bool temp = false;
            if (!File.Exists(path))
                temp = true;
            else
            {
                byte[] array2 = File.ReadAllBytes(path);
                if (!(size == array2.Length && crc == Crc32Algorithm.Compute(array2)))
                    temp = true;
                array2 = null;
            }

            if (temp)
            {
                string text = directoryName + "\\_" + fileName;
                string urlAddress = server + text.Replace("\\", "/");
                log_error(processed_files + " " + path + " " + crc + " " + size + "Download");
                if (!Directory.Exists(str + directoryName))
                    Directory.CreateDirectory(str + directoryName);

                download_file_stable(urlAddress, str + directoryName + "\\_" + fileName, true);
                decompress_file(str + directoryName + "\\_" + fileName, path);
            }
            else
            {
                processed_files++;
                //log_error(processed_files + " "+ path + " " + crc + " " + size + "Good\n");
            }
        }

        private void check_single_file_fast(string file, int size, uint crc)
        {
            string path = UnZip(file);
            string directoryName = System.IO.Path.GetDirectoryName(path);
            string fileName = System.IO.Path.GetFileName(path);
            string str = "NewData\\";
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            bool temp = false;
            if (!File.Exists(path))
                temp = true;
            else
            {
                //byte[] array2 = File.ReadAllBytes(path);
                //if (size != array2.Length || crc != Crc32Algorithm.Compute(array2))
                
                if(size!=new System.IO.FileInfo(path).Length)
                    temp = true;
                //"Client\\biology\\texture\\m12405a.dds"
                //                array2 = null;
            }

            if (temp)
            {
                string text = directoryName + "\\_" + fileName;
                string urlAddress = server + text.Replace("\\", "/");
                if (!Directory.Exists(str + directoryName))
                    Directory.CreateDirectory(str + directoryName);
                log_error( processed_files + " " + path + " " + crc + " " + size + "Download\n");
                download_file_stable(urlAddress, str + directoryName + "\\_" + fileName,true);
                decompress_file(str + directoryName + "\\_" + fileName, path);
            }
            else
            {
                processed_files++;
                //log_error( processed_files + " " + path + " " + crc + " " + size + "Good\n");
            }
        }


        private void decompress_file(string z_path,string t_path)
        {
            byte[] raw = File.ReadAllBytes(z_path);
            try
            {
                byte[] array;
                do_decomp(raw, out array);
                //                byte[] array = DeCompress(raw);
                raw = null;
                if (array != null)
                    File.WriteAllBytes(t_path, array);
            }
            catch (Exception e)
            {
                raw = null;
                log_error("Decompress failed: " + z_path+" "+t_path);
                if (e.InnerException == null)
                    log_error(e.Message);
                else
                    log_error(e.InnerException.Message);
                //File.WriteAllBytes(t_path, raw);
            }
            File.Delete(z_path);
            processed_files++;
        }
        private void do_decomp(byte[] raw,out byte[] rez)
        {
            //var task = Task.Run(() => DeCompress(raw));
            //var a = ZlibStream.UncompressBuffer(File.ReadAllBytes("I:\\games\\Aura Kingdom Frost\\WebBase\\Client\\_n43601 (1).dds"));
//            File.WriteAllBytes("C:\\Users\\sv\\Downloads\\_n43601-1.dds", ZlibStream.CompressBuffer(File.ReadAllBytes("C:\\Users\\sv\\Downloads\\n43601 (1).dds")));
            var task = Task.Run(() => ZlibStream.UncompressBuffer(raw));
            if (task.Wait(TimeSpan.FromSeconds(20)))
            {
                rez= task.Result;
            }
            else
            {
                rez= null;
                throw(new Exception("Decompression timed out"));
            }

            //byte[] rez2;
            //var task2 = Task.Run(() => DeCompress(raw));
            ////var task = Task.Run(() => ZlibStream.UncompressBuffer(raw));
            //if (task.Wait(TimeSpan.FromSeconds(20)))
            //{
            //    rez2 = task.Result;
            //}
            //else
            //{
            //    rez2 = null;
            //    throw (new Exception("Decompression timed out"));
            //}
        }

        public static string UnZip(string value)
        {
            byte[] array = new byte[value.Length];
            int num = 0;
            char[] array2 = value.ToCharArray();
            for (int i = 0; i < array2.Length; i++)
            {
                char c = array2[i];
                array[num++] = (byte)c;
            }
            MemoryStream memoryStream = new MemoryStream(array);
            System.IO.Compression.GZipStream gZipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Decompress);
            array = new byte[array.Length];
            int num2 = gZipStream.Read(array, 0, array.Length);
            StringBuilder stringBuilder = new StringBuilder(num2);
            for (int j = 0; j < num2; j++)
            {
                stringBuilder.Append((char)array[j]);
            }
            gZipStream.Close();
            memoryStream.Close();
            gZipStream.Dispose();
            memoryStream.Dispose();
            return stringBuilder.ToString();
        }
        public static byte[] DeCompress(byte[] raw)
        {
            byte[] result;
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (ZlibStream zlibStream = new ZlibStream(memoryStream, Ionic.Zlib.CompressionMode.Decompress))
                {
                    zlibStream.Write(raw, 0, raw.Length);
                }
                result = memoryStream.ToArray();
            }
            return result;
        /*        public static byte[] DeCompress(byte[] raw)
        {
            byte[] result;
            using (MemoryStream memoryStream = new MemoryStream())
            {
                var a = new zlib.ZOutputStream(memoryStream);
                a.Write(raw, 0, raw.Length);
                result = memoryStream.ToArray();
            }
            return result;
        }*/
        }

        public static string CalculateMD5Hash(string input)
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);
            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }




        public static void update_login(string login, string password)
        {            
            Properties.Settings.Default["last_login"] = login;
            Properties.Settings.Default["last_password"] = password;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
        }

        public static bool check_login(string login, string password)
        {
            DataSet ds = new DataSet();
            DataTable dt = new DataTable();
            bool logged_in = false;
            NpgsqlConnection conn = new NpgsqlConnection(connection_string_1);
            conn.Open();
            string sql = @"select check_login('" + login + "','"+ password + "');";
            NpgsqlDataAdapter da = new NpgsqlDataAdapter(sql, conn);
            ds.Reset();
            da.Fill(ds);
            int a = int.Parse(string.Format("{0}", ds.Tables[0].Rows[0].ItemArray[0]));
            conn.Close();

            if (a == 0)
            {
                logged_in = false;
            }
            else if (a == 1)
            {
                return true;
            }
            else
            {
                throw(new Exception("Got unknown responce: "+a));
            }
            conn.Close();
            return logged_in;
        }

        public static bool register(string login, string password)
        {
            DataSet ds = new DataSet();
            NpgsqlConnection conn = new NpgsqlConnection(connection_string_2);
            conn.Open();
            string sql = @"select go_register('" + login + "','" + password + "','" + CalculateMD5Hash(password).ToLower() + "')";
            NpgsqlDataAdapter da = new NpgsqlDataAdapter(sql, conn);
            ds.Reset();
            da.Fill(ds);
            int a = int.Parse(string.Format("{0}", ds.Tables[0].Rows[0].ItemArray[0]));
            if (a == 0)
            {
                //registered
            }
            else if (a == 1)
            {
                return false;
            }
            else
            {
                throw (new Exception("Got unknown responce: " + a));
            }
            conn.Close();

            DataSet ds1 = new DataSet();
            NpgsqlConnection conn1 = new NpgsqlConnection(connection_string_1);
            conn.Open();
            sql = @"select go_registerlogin('" + login + "','" + password + "')";
            NpgsqlDataAdapter da1 = new NpgsqlDataAdapter(sql, conn1);
            ds1.Reset();
            da1.Fill(ds1);
            a = int.Parse(string.Format("{0}", ds1.Tables[0].Rows[0].ItemArray[0]));
            if (a == 0)
            {
                //registered
            }
            else if (a == 1)
            {
                return false;
            }
            else
            {
                throw (new Exception("Акаунт с таким именем невозможно зарегистрировать. \nGot unknown responce(2): " + a));
            }
            conn.Close();
            return true;
        }

        public static bool default_login()
        {
            return check_login((string)Properties.Settings.Default["last_login"],
                (string)Properties.Settings.Default["last_password"]);
        }

    }

}
