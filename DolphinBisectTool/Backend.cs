using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SevenZip;

namespace DolphinBisectTool
{
    public enum UserInput
    {
        Yes,
        No,
        Cancel
    }

    class Backend
    {
        internal delegate UserInput BisectEventDelegate(int build, bool final_trigger = false);
        internal event BisectEventDelegate BisectEvent;

        internal delegate void BisectErrorDelegate(string e);
        internal event BisectErrorDelegate BisectError;

        internal delegate void UpdateProgressDelegate(int progress_percentage, string ui_text, ProgressBarStyle progress_type);
        internal event UpdateProgressDelegate UpdateProgress;

        int m_first_index;
        int m_second_index;
        List<string> m_build_list;

        public Backend(int first_index, int second_index, List<string> build_list)
        {
            m_first_index = first_index;
            m_second_index = second_index;
            m_build_list = build_list;
        }

        public void Bisect(string boot_title = "")
        {
            int test_index = 0;
            int test_direction = 0;
            int final_build_index_offset = 0;
            List<String> skipped_builds = new List<string>();
            RunBuild run_build = new RunBuild();
            Logger log = new Logger();

            while (!(m_first_index == m_second_index - 1))
            {

                test_index = m_first_index == -1 ? (0 + m_second_index) / 2 : (m_first_index + m_second_index) / 2;

                // dumb thing to make sure we keep trying to download a build until we get a valid build
                do
                {
                    try
                    {
                        Download(m_build_list[test_index]);
                        log.Write("Testing build " + m_build_list[test_index]);
                        break;
                    }
                    catch (Exception e)
                    {
                        log.Write("ERROR. Skipping build " + m_build_list[test_index]);
                        skipped_builds.Add(m_build_list[test_index]);
                        BisectError(e.Message);
                        if (test_direction == 0)
                            --test_index;
                        else
                            ++test_index;
                    }
                }
                while (true);

                if (!string.IsNullOrEmpty(boot_title))
                    run_build.Run(boot_title);
                else
                    run_build.Run();

                UserInput return_val = BisectEvent(test_index);

                if (return_val == UserInput.Yes)
                {
                    log.Write("Build " + m_build_list[test_index] + " marked as a BAD build");
                    m_first_index = test_index;
                    test_direction = 1;
                    final_build_index_offset = 0;
                }
                else if (return_val == UserInput.No)
                {
                    log.Write("Build " + m_build_list[test_index] + " marked as a GOOD build");
                    m_second_index = test_index;
                    test_direction = 0;
                    final_build_index_offset = 1;
                }
                else
                    return;
            }

            log.Write("Bisect completed. " + m_build_list[test_index - final_build_index_offset]
                      + " may be the culprit.");
            if (!(skipped_builds.Count == 0))
            {
                string sb = string.Join(", ", skipped_builds.ToArray());
                log.Write("Skipped builds: " + sb);
                log.Dispose();
            }
            UserInput open_url = BisectEvent(test_index, true);

            if (open_url == UserInput.Yes)
            {
                Process.Start("https://dolp.in/" + m_build_list[test_index-final_build_index_offset]);
            }
        }

        public void Download(string version)
        {
            // Windows will throw an error if you have the folder you're trying to delete open in
            // explorer. It will remove the contents but error out on the folder removal. That's
            // good enough but this is just so it doesn't crash.
            try
            {
                if (Directory.Exists(@"dolphin"))
                    Directory.Delete(@"dolphin", true);
            }
            catch (IOException)
            {
            }

            using (WebClient client = new WebClient())
            {
                Uri redirect_uri = new Uri("https://dolp.in/v" + version);
                String information_html = client.DownloadString(redirect_uri);
                String regex = "https://dl\\.dolphin-emu\\.org/builds/"
                + "[0-9a-f]{2}/[0-9a-f]{2}/dolphin-master-" + version + "-x64\\.7z";
                Match m = Regex.Match(information_html, regex);
                if (!m.Success)
                {
                    throw new Exception("Windows build for version " + version + " not found");
                }
                String build_url = m.Value;

                client.DownloadProgressChanged += (s, e) =>
                {
                    UpdateProgress(e.ProgressPercentage, "Downloading build", ProgressBarStyle.Continuous);
                };

                client.DownloadFileAsync(new Uri(build_url), "dolphin.7z");

                while (client.IsBusy)
                {
                    Application.DoEvents();
                }

                SevenZipExtractor dolphin_zip = new SevenZipExtractor(@"dolphin.7z");

                dolphin_zip.Extracting += (sender, eventArgs) =>
                {
                    UpdateProgress(eventArgs.PercentDone, "Extracting and launching", ProgressBarStyle.Continuous);
                };

                try
                {
                    dolphin_zip.ExtractArchive("dolphin");
                }
                catch (Exception e)
                {
                    throw new Exception("Error extracting. Probably a missing build. Skipping this build.", e);
                }
            }
        }
    }
}
