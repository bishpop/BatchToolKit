using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ImageMagick;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace BatchToolkit
{
    public partial class MainWindow : Window
    {
        private List<string> selectedFiles = new List<string>();
        private string currentMode = "TabPngDds";

        public MainWindow()
        {
            InitializeComponent();
            SetActiveNavButton(BtnNavPngDds);
        }

        // --- Custom Window Controls ---
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // --- Custom Message Box Modal ---
        private void ShowModal(string title, string message, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                ModalTitle.Text = title;
                ModalMessage.Text = message;
                ModalIcon.Text = isError ? "⚠️" : "ℹ️";
                ModalOverlay.Visibility = Visibility.Visible;
            });
        }

        private void CloseModal_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        // --- Navigation Logic ---
        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            var clickedBtn = sender as Button;
            if (clickedBtn == null) return;

            SetActiveNavButton(clickedBtn);
            currentMode = clickedBtn.Tag.ToString();

            selectedFiles.Clear();
            UpdateFileList();
            ProgressTask.Value = 0;
            ProgressLabel.Text = "Ready.";

            OptPng2Dds.Visibility = Visibility.Collapsed;
            OptRename.Visibility = Visibility.Collapsed;

            switch (currentMode)
            {
                case "TabPngDds":
                    HeaderTitle.Text = "PNG → DDS";
                    HeaderSub.Text = "Convert PNGs to DirectDraw Surface textures";
                    BtnAction.Content = "Start Conversion";
                    OptPng2Dds.Visibility = Visibility.Visible;
                    break;
                case "TabDdsPng":
                    HeaderTitle.Text = "DDS → PNG";
                    HeaderSub.Text = "Extract DDS textures to standard PNGs";
                    BtnAction.Content = "Start Conversion";
                    break;
                case "TabGifPng":
                    HeaderTitle.Text = "GIF → PNG";
                    HeaderSub.Text = "Extract all frames from animated GIFs";
                    BtnAction.Content = "Extract Frames";
                    break;
                case "TabRename":
                    HeaderTitle.Text = "Mass Rename";
                    HeaderSub.Text = "Smart file renaming with auto-increment";
                    BtnAction.Content = "Rename Files";
                    OptRename.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void SetActiveNavButton(Button activeBtn)
        {
            BtnNavPngDds.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
            BtnNavDdsPng.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
            BtnNavGifPng.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
            BtnNavRename.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));

            activeBtn.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        }

        // --- File Management ---
        private string GetCurrentFilter()
        {
            if (currentMode == "TabPngDds") return "PNG Files (*.png)|*.png|All files (*.*)|*.*";
            if (currentMode == "TabDdsPng") return "DDS Files (*.dds)|*.dds|All files (*.*)|*.*";
            if (currentMode == "TabGifPng") return "GIF Files (*.gif)|*.gif|All files (*.*)|*.*";
            return "All files (*.*)|*.*";
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = GetCurrentFilter()
            };

            if (openFileDialog.ShowDialog() == true)
            {
                selectedFiles.AddRange(openFileDialog.FileNames);
                selectedFiles = selectedFiles.Distinct().ToList();
                UpdateFileList();
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            dialog.Title = "Select Folder to Scan";

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string ext = "*.*";
                if (currentMode == "TabPngDds") ext = "*.png";
                if (currentMode == "TabDdsPng") ext = "*.dds";
                if (currentMode == "TabGifPng") ext = "*.gif";

                var files = Directory.GetFiles(dialog.FileName, ext).ToList();
                selectedFiles.AddRange(files);
                selectedFiles = selectedFiles.Distinct().ToList();
                UpdateFileList();
            }
        }

        private void UpdateFileList()
        {
            FileList.Items.Clear();
            foreach (var file in selectedFiles)
            {
                FileList.Items.Add(Path.GetFileName(file));
            }
            StatusText.Text = selectedFiles.Count > 0 ? $"📄 {selectedFiles.Count} file(s) ready." : "No files selected.";
        }

        // --- Core Execution Router ---
        private async void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (selectedFiles.Count == 0)
            {
                ShowModal("Warning", "Please add files to the list first.", true);
                return;
            }

            BtnAction.IsEnabled = false;
            ProgressTask.Value = 0;

            if (currentMode == "TabRename")
            {
                string pattern = TxtPattern.Text.Trim();
                if (string.IsNullOrEmpty(pattern))
                {
                    ShowModal("Warning", "Please enter a valid naming pattern.", true);
                    BtnAction.IsEnabled = true;
                    return;
                }
                await RunBatchRenameAsync(pattern);
            }
            else if (currentMode == "TabGifPng")
            {
                await RunGifExtractionAsync();
            }
            else
            {
                await RunConversionAsync();
            }

            BtnAction.IsEnabled = true;
        }

        // --- 1. & 2. Magick Conversion Logic (PNG <-> DDS) ---
        private async Task RunConversionAsync()
        {
            bool isPngToDds = currentMode == "TabPngDds";

            // Read UI Dropdown values safely
            string selectedRes = "";
            string selectedComp = "";
            Dispatcher.Invoke(() => {
                if (isPngToDds)
                {
                    selectedRes = ((ComboBoxItem)CmbResolution.SelectedItem).Content.ToString();
                    selectedComp = ((ComboBoxItem)CmbCompression.SelectedItem).Content.ToString();
                }
            });

            int total = selectedFiles.Count;
            int success = 0;
            string lastError = "";

            await Task.Run(() =>
            {
                for (int i = 0; i < total; i++)
                {
                    string path = selectedFiles[i];
                    string dir = Path.GetDirectoryName(path);
                    string name = Path.GetFileNameWithoutExtension(path);
                    string targetExt = isPngToDds ? ".dds" : ".png";

                    string outDir = Path.Combine(dir, isPngToDds ? "Converted_DDS" : "Converted_PNG");
                    Directory.CreateDirectory(outDir);
                    string outPath = Path.Combine(outDir, name + targetExt);

                    try
                    {
                        using (var image = new MagickImage(path))
                        {
                            if (isPngToDds)
                            {
                                // --- RESIZING LOGIC ---
                                if (selectedRes != "Original Size")
                                {
                                    var parts = selectedRes.Split('x');
                                    // HIER IST DER FIX: uint statt int nutzen!
                                    if (parts.Length == 2 && uint.TryParse(parts[0], out uint w) && uint.TryParse(parts[1], out uint h))
                                    {
                                        // IgnoreAspectRatio zwingt das Bild exakt in die Texturmaße (z.B. 28x28)
                                        image.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
                                    }
                                }

                                image.Format = MagickFormat.Dds;
                                image.ColorType = ColorType.TrueColorAlpha;
                                image.Depth = 8;

                                // --- COMPRESSION LOGIC ---
                                if (selectedComp.Contains("Lossless"))
                                {
                                    image.Settings.SetDefine(MagickFormat.Dds, "compression", "none");
                                }
                                else if (selectedComp.Contains("BC3"))
                                {
                                    image.Settings.SetDefine(MagickFormat.Dds, "compression", "dxt5");
                                }
                                else if (selectedComp.Contains("BC1"))
                                {
                                    image.Settings.SetDefine(MagickFormat.Dds, "compression", "dxt1");
                                }
                            }
                            else
                            {
                                image.Format = MagickFormat.Png;
                            }
                            image.Write(outPath);
                        }
                        success++;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }

                    UpdateProgress(i + 1, total, "Converting");
                }
            });

            if (success == 0 && total > 0)
            {
                ShowModal("Conversion Failed", $"Something went wrong. Engine reported:\n\n{lastError}", true);
            }
            else
            {
                ShowModal("Task Complete", $"✅ {success} of {total} files converted successfully.");
            }
        }

        // --- 3. GIF Extraction Logic ---
        private async Task RunGifExtractionAsync()
        {
            int total = selectedFiles.Count;
            int success = 0;
            string lastError = "";

            await Task.Run(() =>
            {
                for (int i = 0; i < total; i++)
                {
                    string path = selectedFiles[i];
                    string dir = Path.GetDirectoryName(path);
                    string name = Path.GetFileNameWithoutExtension(path);
                    string outDir = Path.Combine(dir, name);
                    Directory.CreateDirectory(outDir);

                    try
                    {
                        using (var collection = new MagickImageCollection(path))
                        {
                            int frameCount = 0;
                            foreach (var frame in collection)
                            {
                                string framePath = Path.Combine(outDir, $"frame_{frameCount:D4}.png");
                                frame.Format = MagickFormat.Png;
                                frame.Write(framePath);
                                frameCount++;
                            }
                        }
                        success++;
                    }
                    catch (Exception ex) { lastError = ex.Message; }

                    UpdateProgress(i + 1, total, "Extracting GIF");
                }
            });

            if (success == 0 && total > 0)
                ShowModal("Extraction Failed", $"Could not process GIF.\n\n{lastError}", true);
            else
                ShowModal("Task Complete", $"✅ Extracted frames from {success} of {total} GIFs.");
        }

        // --- 4. Batch Rename Logic ---
        private async Task RunBatchRenameAsync(string pattern)
        {
            int total = selectedFiles.Count;
            int success = 0;

            var match = Regex.Match(pattern, @"^(.*?)(\d+)$");
            string prefix = match.Success ? match.Groups[1].Value : pattern;
            string numStr = match.Success ? match.Groups[2].Value : "001";
            int pad = numStr.Length;
            int startNum = int.TryParse(numStr, out int n) ? n : 1;

            await Task.Run(() =>
            {
                List<string> tempFiles = new List<string>();
                foreach (string path in selectedFiles)
                {
                    string dir = Path.GetDirectoryName(path);
                    string ext = Path.GetExtension(path);
                    string tempPath = Path.Combine(dir, $"temp_{Guid.NewGuid():N}{ext}");
                    File.Move(path, tempPath);
                    tempFiles.Add(tempPath);
                }

                List<string> finalFiles = new List<string>();
                for (int i = 0; i < tempFiles.Count; i++)
                {
                    string tempPath = tempFiles[i];
                    string dir = Path.GetDirectoryName(tempPath);
                    string ext = Path.GetExtension(tempPath);
                    string newName = $"{prefix}{(startNum + i).ToString().PadLeft(pad, '0')}{ext}";
                    string finalPath = Path.Combine(dir, newName);

                    try
                    {
                        File.Move(tempPath, finalPath);
                        finalFiles.Add(finalPath);
                        success++;
                    }
                    catch (Exception)
                    {
                        finalFiles.Add(tempPath);
                    }

                    UpdateProgress(i + 1, total, "Renaming");
                }

                Dispatcher.Invoke(() =>
                {
                    selectedFiles = finalFiles;
                    UpdateFileList();
                });
            });

            ShowModal("Task Complete", $"✅ {success} of {total} files successfully renamed.");
        }

        // --- Helper Methods ---
        private void UpdateProgress(int current, int total, string taskName)
        {
            int percent = (int)(((double)current / total) * 100);
            Dispatcher.Invoke(() =>
            {
                ProgressTask.Value = percent;
                ProgressLabel.Text = $"{taskName} {current} / {total}...";
            });
        }
    }
}