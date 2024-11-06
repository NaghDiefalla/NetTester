using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using LiveCharts;
using LiveCharts.Wpf;
using Newtonsoft.Json;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using System.Text;
using Microsoft.Win32;

namespace NetTester
{
    public partial class MainWindow : Window
    {
        #region Definitions/StupidShit

        private List<PingResult> _pingResults;
        private const string ConnectionString = "Data Source=pingHistory.db;Version=3;";
        private CancellationTokenSource _cancellationTokenSource;
        private bool isTesting;
        private UserProfile _userProfile;

        public MainWindow()
        {
            InitializeComponent();
            InitializeDatabase();
            InitializeUI(); // Separate method for UI initialization
            _pingResults = new List<PingResult>();
        }


        #endregion

        #region Chart

        private void UpdateChart()
        {
            pingChart.Series.Clear();

            // Ping Response Times
            var responseTimeSeries = new LineSeries
            {
                Title = "Ping Response Times (ms)",
                Values = new ChartValues<double>()
            };

            // Packet Loss
            var packetLossSeries = new LineSeries
            {
                Title = "Packet Loss (%)",
                Values = new ChartValues<double>()
            };

            // Jitter
            var jitterSeries = new LineSeries
            {
                Title = "Jitter (ms)",
                Values = new ChartValues<double>()
            };

            // Delay
            //var delaySeries = new LineSeries
            //{
            //    Title = "Delay (ms)",
            //    Values = new ChartValues<double>()
            //};

            // Latency
            var latencySeries = new LineSeries
            {
                Title = "Latency (ms)",
                Values = new ChartValues<double>()
            };

            // Speed
            var speedSeries = new LineSeries
            {
                Title = "Speed (Mbps)", // Adjust this title as needed
                Values = new ChartValues<double>()
            };

            foreach (var result in _pingResults)
            {
                if (result.ResponseTime != -1)
                    responseTimeSeries.Values.Add((double)result.ResponseTime);

                packetLossSeries.Values.Add(result.PacketLoss);
                jitterSeries.Values.Add(result.Jitter);
                //delaySeries.Values.Add(result.Delay);
                latencySeries.Values.Add(result.Latency);
                speedSeries.Values.Add(result.Speed); // Assuming speed is calculated elsewhere
            }

            pingChart.Series.Add(responseTimeSeries);
            pingChart.Series.Add(packetLossSeries);
            pingChart.Series.Add(jitterSeries);
            //pingChart.Series.Add(delaySeries);
            pingChart.Series.Add(latencySeries);
            //pingChart.Series.Add(speedSeries);
        }

        #endregion

        #region Handlers

        private async void btnPing_Click(object sender, RoutedEventArgs e)
        {
            await StartPingTestAsync();
        }

        private async void btnSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            var result = await RunNetworkSpeedTestAsync();
            MessageBox.Show($"Download Speed: {result.DownloadSpeedMbps} Mbps\nUpload Speed: {result.UploadSpeedMbps} Mbps");
        }

        private async void btnTraceroute_Click(object sender, RoutedEventArgs e)
        {
            lstTracerouteResults.Items.Clear(); // Clear previous results
            string address = txtAddress.Text; // Get the IP or hostname from the input
            txtStatus.Text = "Running traceroute...";

            try
            {
                var result = await RunTracerouteAsync(address);

                // Add each hop to the list
                foreach (var hop in result)
                {
                    lstTracerouteResults.Items.Add(hop);
                }

                // Save the traceroute results to the database
                SaveTracerouteResults(result);

                txtStatus.Text = "Traceroute completed.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Traceroute failed: {ex.Message}");
                txtStatus.Text = "Traceroute failed.";
            }
        }


        private void btnDNSLookup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ipHostInfo = Dns.GetHostEntry(txtAddress.Text);
                MessageBox.Show($"DNS Lookup Successful: {ipHostInfo.HostName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DNS Lookup Failed: {ex.Message}");
            }
        }

        private void btnSaveHistory_Click(object sender, RoutedEventArgs e)
        {
            string history = GetPingHistory();
            string filePath = "History.txt";

            try
            {
                File.WriteAllText(filePath, history);
                MessageBox.Show("History saved successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving history: {ex.Message}");
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopPingTest();
        }

        #endregion

        #region Export

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            // Prompt user for the desired export format
            var result = MessageBox.Show("Would you like to export as PDF? Click 'No' for JSON.", "Export Format", MessageBoxButton.YesNoCancel);

            if (result == MessageBoxResult.Cancel) return;

            // Create a SaveFileDialog to allow user to select save location and file name
            SaveFileDialog saveFileDialog = new SaveFileDialog();

            if (result == MessageBoxResult.Yes) // Export to PDF
            {
                saveFileDialog.Filter = "PDF Files (*.pdf)|*.pdf";
                saveFileDialog.FileName = $"NetTester_{GenerateRandomString(6)}.pdf";
            }
            else // Export to JSON
            {
                saveFileDialog.Filter = "JSON Files (*.json)|*.json";
                saveFileDialog.FileName = $"NetTester_{GenerateRandomString(6)}.json";
            }

            // Show the SaveFileDialog and check if user selected a file
            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                try
                {
                    if (result == MessageBoxResult.Yes) // PDF export logic
                    {
                        using (PdfDocument document = new PdfDocument())
                        {
                            PdfPage page = document.AddPage();
                            XGraphics gfx = XGraphics.FromPdfPage(page);
                            XFont font = new XFont("Verdana", 20, XFontStyleEx.Bold);
                            XFont fontSmall = new XFont("Verdana", 12, XFontStyleEx.Regular);

                            gfx.DrawString("Network Testing Export", font, XBrushes.Black, new XRect(0, 20, page.Width, page.Height), XStringFormats.TopCenter);
                            gfx.DrawString($"Exported on: {DateTime.Now}", fontSmall, XBrushes.Black, new XRect(0, 60, page.Width, page.Height), XStringFormats.TopCenter);

                            // Collect traceroute results from the database
                            string tracerouteResults = GetTracerouteHistory();
                            gfx.DrawString("Traceroute Results:", fontSmall, XBrushes.Black, new XRect(20, 100, page.Width, page.Height), XStringFormats.TopLeft);

                            // Check if there are results
                            if (string.IsNullOrWhiteSpace(tracerouteResults))
                            {
                                tracerouteResults = "No traceroute results available.";
                            }

                            // Draw traceroute results
                            double tracerouteYPosition = 120; // Starting position for traceroute results
                            string[] tracerouteLines = tracerouteResults.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                            foreach (var line in tracerouteLines)
                            {
                                if (tracerouteYPosition + fontSmall.GetHeight() > page.Height - 40) // Check if we need a new page
                                {
                                    page = document.AddPage();
                                    gfx = XGraphics.FromPdfPage(page);
                                    tracerouteYPosition = 20; // Reset position for new page
                                }

                                gfx.DrawString(line, fontSmall, XBrushes.Black, new XRect(20, tracerouteYPosition, page.Width - 40, page.Height), XStringFormats.TopLeft);
                                tracerouteYPosition += fontSmall.GetHeight(); // Move to the next line
                            }

                            // Proceed with Ping Results (similar to traceroute)
                            gfx.DrawString("Ping Results:", fontSmall, XBrushes.Black, new XRect(20, tracerouteYPosition + 20, page.Width, page.Height), XStringFormats.TopLeft);
                            string history = GetPingHistory();

                            string[] historyLines = history.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                            Array.Reverse(historyLines); // Reverse to display latest results first

                            // Start drawing the reversed history
                            double currentYPosition = tracerouteYPosition + 40; // Set starting position after traceroute results

                            foreach (string line in historyLines)
                            {
                                if (currentYPosition + fontSmall.GetHeight() > page.Height - 40) // Check if we need a new page
                                {
                                    page = document.AddPage();
                                    gfx = XGraphics.FromPdfPage(page);
                                    currentYPosition = 20; // Reset position for new page
                                }

                                gfx.DrawString(line, fontSmall, XBrushes.Black, new XRect(20, currentYPosition, page.Width - 40, page.Height), XStringFormats.TopLeft);
                                currentYPosition += fontSmall.GetHeight(); // Move to the next line
                            }

                            document.Save(filePath);
                        }

                        MessageBox.Show($"Exported to PDF successfully at {filePath}!");
                    }
                    else // Export to JSON
                    {
                        // Collect traceroute results and ping history
                        var exportData = new
                        {
                            Timestamp = DateTime.Now,
                            TracerouteResults = GetTracerouteHistory(),
                            PingResults = GetPingHistory()
                        };

                        // Serialize to JSON
                        string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented);

                        // Write to file
                        File.WriteAllText(filePath, jsonData);
                        MessageBox.Show($"Exported to JSON successfully at {filePath}!");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export: {ex.Message}");
                }
            }
        }






        #endregion

        #region Theme

        private void themeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (themeSelector.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedTheme = selectedItem.Content.ToString();
                ApplyTheme(selectedTheme);
            }
        }

        private void ApplyTheme(string theme)
        {
            ResourceDictionary newTheme = new ResourceDictionary();

            // Clear the existing theme
            Application.Current.Resources.MergedDictionaries.Clear();

            // Load the new theme based on the selection
            switch (theme)
            {
                case "Light":
                    newTheme.Source = new Uri("LightTheme.xaml", UriKind.Relative);
                    break;
                case "Dark":
                    newTheme.Source = new Uri("DarkTheme.xaml", UriKind.Relative);
                    break;
                case "High Contrast":
                    newTheme.Source = new Uri("HighContrastTheme.xaml", UriKind.Relative);
                    break;
                default:
                    newTheme.Source = new Uri("LightTheme.xaml", UriKind.Relative);
                    break;
            }

            // Add the new theme to the application resources
            Application.Current.Resources.MergedDictionaries.Add(newTheme);
        }


        #endregion

        #region Methods

        private string GetTracerouteHistory()
        {
            StringBuilder sb = new StringBuilder();

            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                string selectQuery = "SELECT HopNumber, ResponseTime, IPAddress, Address, Timestamp FROM TracerouteHistory ORDER BY Timestamp DESC";

                using (var command = new SQLiteCommand(selectQuery, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int hopNumber = reader.GetInt32(0); // Assuming HopNumber is the first column
                            long responseTime = reader.GetInt32(1); // Assuming ResponseTime is the second column
                            string ipAddress = reader.GetString(2); // Assuming IPAddress is the third column
                            string address = reader.GetString(3); // Assuming Address is the fourth column

                            sb.AppendLine($"Hop Number: {hopNumber}, Response Time: {responseTime} ms, Address: {address} ({ipAddress})");
                        }
                    }
                }
            }

            return sb.ToString();
        }


        private void SaveTracerouteResults(List<TracerouteHop> hops)
        {
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                foreach (var hop in hops)
                {
                    string insertQuery = "INSERT INTO TracerouteHistory (HopNumber, ResponseTime, IPAddress, Address) VALUES (@HopNumber, @ResponseTime, @IPAddress, @Address)";
                    using (var command = new SQLiteCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@HopNumber", hop.HopNumber);
                        command.Parameters.AddWithValue("@ResponseTime", hop.ResponseTime);
                        command.Parameters.AddWithValue("@IPAddress", hop.IPAddress);
                        command.Parameters.AddWithValue("@Address", hop.Address);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }


        private void DisplayTracerouteResults(List<TracerouteHop> hops)
        {
            foreach (var hop in hops)
            {
                lstTracerouteResults.Items.Add($"Hop: {hop.HopNumber}, IP: {hop.IPAddress}, Response Time: {hop.ResponseTime} ms, Host: {hop.Address}");
            }
        }


        private async Task StartPingTestAsync()
        {
            ToggleButtons(false);
            isTesting = true;

            txtStatus.Text = "Pinging...";
            var ping = new Ping();
            int packetCount = int.Parse(txtPacketCount.Text);
            int timeout = int.Parse(txtTimeout.Text);
            _pingResults.Clear();
            _cancellationTokenSource = new CancellationTokenSource();

            int successfulPings = 0;
            List<int> responseTimes = new List<int>(); // For calculating jitter, delay, and latency

            for (int i = 0; i < packetCount; i++)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    txtStatus.Text = "Ping test stopped.";
                    break;
                }

                try
                {
                    var result = await ping.SendPingAsync(txtAddress.Text, timeout);
                    var pingResult = new PingResult
                    {
                        Address = txtAddress.Text,
                        ResponseTime = result.Status == IPStatus.Success ? (int)result.RoundtripTime : -1,
                        Timestamp = DateTime.Now,
                        PacketLoss = 0, // Initial packet loss set to 0
                        Jitter = 0, // Placeholder for jitter
                        Delay = 0, // Placeholder for delay
                        Latency = 0, // Placeholder for latency
                        Speed = 0 // Placeholder for speed
                    };

                    if (result.Status == IPStatus.Success)
                    {
                        successfulPings++;
                        responseTimes.Add(pingResult.ResponseTime); // Collect response times for jitter calculation
                    }

                    _pingResults.Add(pingResult);
                    SavePingHistory(pingResult);
                    txtStatus.Text = $"Pinged {pingResult.Address}: {pingResult.ResponseTime} ms";
                }
                catch (Exception ex)
                {
                    txtStatus.Text = $"Error: {ex.Message}";
                }

                await Task.Delay(int.Parse(txtRefreshInterval.Text));
            }

            // Calculate packet loss percentage
            double packetLossPercentage = ((packetCount - successfulPings) / (double)packetCount) * 100;

            // Calculate Jitter (standard deviation of response times)
            double jitter = CalculateJitter(responseTimes);
            double averageResponseTime = responseTimes.Count > 0 ? responseTimes.Average() : 0;

            // Update each ping result with the calculated metrics
            foreach (var result in _pingResults)
            {
                result.PacketLoss = packetLossPercentage;
                result.Jitter = jitter;
                result.Delay = averageResponseTime; // Placeholder: Adjust based on your logic
                result.Latency = averageResponseTime; // Placeholder: Adjust based on your logic
                result.Speed = 0; // Placeholder: Adjust based on your logic
            }

            // Update the chart and display results
            UpdateChart();
            DisplayResults();
            StopPingTest();
        }

        private double CalculateJitter(List<int> responseTimes)
        {
            if (responseTimes.Count < 2)
                return 0;

            double jitter = 0;
            for (int i = 1; i < responseTimes.Count; i++)
            {
                jitter += Math.Abs(responseTimes[i] - responseTimes[i - 1]);
            }

            return jitter / (responseTimes.Count - 1); // Average jitter
        }

        private void DisplayLatencyAnalysis()
        {
            if (_pingResults.Count == 0) return;

            int totalResponses = 0;
            int totalTime = 0;
            int minTime = int.MaxValue;
            int maxTime = int.MinValue;

            foreach (var result in _pingResults)
            {
                if (result.ResponseTime != -1)
                {
                    totalResponses++;
                    totalTime += result.ResponseTime;
                    if (result.ResponseTime < minTime) minTime = result.ResponseTime;
                    if (result.ResponseTime > maxTime) maxTime = result.ResponseTime;
                }
            }

            double averageLatency = totalResponses > 0 ? (double)totalTime / totalResponses : 0;

            //txtLatencyAnalysis.Text = $"Average Latency: {averageLatency} ms\n" +
            //                          $"Min Latency: {minTime} ms\n" +
            //                          $"Max Latency: {maxTime} ms";
        }

        private async Task<List<TracerouteHop>> RunTracerouteAsync(string address)
        {
            var hops = new List<TracerouteHop>();
            using (var ping = new Ping())
            {
                int maxHops = 30; // Maximum number of hops
                int timeout = 1000; // Timeout for each ping in milliseconds

                for (int ttl = 1; ttl <= maxHops; ttl++)
                {
                    try
                    {
                        var options = new PingOptions(ttl, true);
                        var reply = await ping.SendPingAsync(address, timeout, new byte[32], options);

                        var hop = new TracerouteHop
                        {
                            HopNumber = ttl,
                            ResponseTime = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1,
                            IPAddress = reply.Address?.ToString() ?? "N/A",
                            Address = reply.Status == IPStatus.Success
                                ? await GetHostNameAsync(reply.Address) // Async hostname resolution
                                : "N/A"
                        };

                        hops.Add(hop);

                        // If we reach the destination, break out of the loop
                        if (reply.Status == IPStatus.Success)
                        {
                            break;
                        }
                    }
                    catch (PingException pingEx)
                    {
                        // Handle ping-specific exceptions (e.g., timeout)
                        hops.Add(new TracerouteHop
                        {
                            HopNumber = ttl,
                            ResponseTime = -1,
                            IPAddress = "N/A",
                            Address = "Ping failed: " + pingEx.Message
                        });
                    }
                    catch (Exception ex)
                    {
                        // Handle general exceptions
                        hops.Add(new TracerouteHop
                        {
                            HopNumber = ttl,
                            ResponseTime = -1,
                            IPAddress = "Error",
                            Address = ex.Message
                        });
                    }
                }
            }

            return hops;
        }

        // Method to asynchronously resolve hostname
        private async Task<string> GetHostNameAsync(IPAddress address)
        {
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(address);
                return hostEntry.HostName;
            }
            catch
            {
                return "N/A"; // Return N/A if resolution fails
            }
        }



        private async Task<NetworkSpeedTestResult> RunNetworkSpeedTestAsync()
        {
            var speedTestResult = new NetworkSpeedTestResult();
            var webClient = new WebClient();

            string downloadUrl = "https://ash-speed.hetzner.com/100MB.bin";
            string uploadUrl = "http://httpbin.org/post"; // Replace with a valid upload URL

            // Measure download speed
            var downloadStartTime = DateTime.Now;
            var downloadData = await webClient.DownloadDataTaskAsync(downloadUrl);
            var downloadEndTime = DateTime.Now;
            double downloadSeconds = (downloadEndTime - downloadStartTime).TotalSeconds;
            speedTestResult.DownloadSpeedMbps = (downloadData.Length * 8 / downloadSeconds) / 1_000_000; // Convert to Mbps

            // Measure upload speed
            byte[] uploadData = new byte[10 * 1024 * 1024]; // 10MB dummy data
            var uploadStartTime = DateTime.Now;
            await webClient.UploadDataTaskAsync(uploadUrl, "POST", uploadData);
            var uploadEndTime = DateTime.Now;
            double uploadSeconds = (uploadEndTime - uploadStartTime).TotalSeconds;
            speedTestResult.UploadSpeedMbps = (uploadData.Length * 8 / uploadSeconds) / 1_000_000; // Convert to Mbps

            return speedTestResult;
        }

        private string GetPingHistory()
        {
            StringBuilder sb = new StringBuilder();
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                string selectQuery = "SELECT Address, ResponseTime, Timestamp FROM PingHistory ORDER BY Timestamp DESC";
                using (var command = new SQLiteCommand(selectQuery, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string address = reader["Address"].ToString();
                            int responseTime = Convert.ToInt32(reader["ResponseTime"]);
                            DateTime timestamp = Convert.ToDateTime(reader["Timestamp"]);

                            sb.AppendLine($"Address: {address}, Response Time: {responseTime} ms, Timestamp: {timestamp}");
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private void SavePingHistory(PingResult pingResult)
        {
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                string insertQuery = "INSERT INTO PingHistory (Address, ResponseTime) VALUES (@Address, @ResponseTime)";
                using (var command = new SQLiteCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Address", pingResult.Address);
                    command.Parameters.AddWithValue("@ResponseTime", pingResult.ResponseTime);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void LoadUserProfile()
        {
            string filePath = "userProfile.json";

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    _userProfile = JsonConvert.DeserializeObject<UserProfile>(json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load user profile: {ex.Message}");
                }
            }
            else
            {
                _userProfile = new UserProfile
                {
                    Theme = "Light",
                    LastIpAddress = "1.1.1.1"
                };
            }
        }

        private void StopPingTest()
        {
            _cancellationTokenSource?.Cancel();
            isTesting = false;
            ToggleButtons(true);
        }

        private void DisplayResults()
        {
            lstResults.Items.Clear();
            foreach (var result in _pingResults)
            {
                lstResults.Items.Add($"Address: {result.Address}, Response Time: {result.ResponseTime} ms, Timestamp: {result.Timestamp}");
            }
        }

        private void ToggleButtons(bool isEnabled)
        {
            btnPing.IsEnabled = isEnabled;
            btnTraceroute.IsEnabled = isEnabled;
            btnDNSLookup.IsEnabled = isEnabled;
            btnExport.IsEnabled = isEnabled;
            //btnSaveHistory.IsEnabled = isEnabled;
            txtPacketCount.IsEnabled = isEnabled;
            txtAddress.IsEnabled = isEnabled;
            txtRefreshInterval.IsEnabled = isEnabled;
            txtTimeout.IsEnabled = isEnabled;
            //txtPacketSize.IsEnabled = isEnabled;
            txtTimeout.IsEnabled = isEnabled;
            themeSelector.IsEnabled = isEnabled;

            btnStop.IsEnabled = true;
        }

        private string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private void InitializeUI()
        {
            // Load user profile
            LoadUserProfile();
            if (_userProfile != null)
            {
                ApplyTheme(_userProfile.Theme);
                txtAddress.Text = _userProfile.LastIpAddress;
            }

            // Set default values
            txtPacketCount.Text = "5";
            txtTimeout.Text = "1000";
            txtRefreshInterval.Text = "1000";
        }


        #endregion

        #region Database

        private void InitializeDatabase()
        {
            try
            {
                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();

                    // Create the PingHistory table if it doesn't exist
                    string createTableQuery = "CREATE TABLE IF NOT EXISTS PingHistory (Id INTEGER PRIMARY KEY, Address TEXT, ResponseTime INTEGER, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP)";
                    using (var command = new SQLiteCommand(createTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    // Create the TracerouteResults table if it doesn't exist
                    string createTable2Query = "CREATE TABLE IF NOT EXISTS TracerouteHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, HopNumber INTEGER NOT NULL, ResponseTime INTEGER NOT NULL, IPAddress TEXT NOT NULL, Address TEXT NOT NULL, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP)";
                    using (var command = new SQLiteCommand(createTable2Query, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SQLiteException ex)
            {
                MessageBox.Show($"Database error: {ex.Message}");
            }
        }


        #endregion
    }

    #region Classes

    public class PingResult
    {
        public string Address { get; set; }
        public int ResponseTime { get; set; } // Ping
        public DateTime Timestamp { get; set; }
        public double PacketLoss { get; set; } // Packet Loss
        public double Jitter { get; set; } // Jitter
        public double Delay { get; set; } // Delay
        public double Latency { get; set; } // Latency
        public double Speed { get; set; } // Speed
    }


    public class UserProfile
    {
        public string Theme { get; set; }
        public string LastIpAddress { get; set; }
    }

    public class NetworkSpeedTestResult
    {
        public double DownloadSpeedMbps { get; set; }
        public double UploadSpeedMbps { get; set; }
    }

    public class TracerouteHop
    {
        public int HopNumber { get; set; }
        public long ResponseTime { get; set; }
        public string IPAddress { get; set; }
        public string Address { get; set; }

        public override string ToString()
        {
            return $"Hop {HopNumber}: {Address} ({IPAddress}) - Response Time: {ResponseTime} ms";
        }
    }


    #endregion

}
