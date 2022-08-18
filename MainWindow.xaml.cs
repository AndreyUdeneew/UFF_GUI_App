using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SpinnakerNET;
using SpinnakerNET.GUI;
using SpinnakerNET.GUI.WPFControls;
using SpinnakerNET.GenApi;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Threading;


namespace SimplestSpinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        IManagedCamera SpinCamColor = null;
        //PropertyGridControl gridControl = new PropertyGridControl();

        Thread RefreshThread;
        public MainWindow()
        {
            InitializeComponent();
    
           //LayoutLeft.Children.Add(gridControl);
        
            //Camera search and initialization
            // Retrieve singleton reference to system object
            ManagedSystem system = new ManagedSystem();

                
            // Retrieve list of cameras from the system
            IList<IManagedCamera> cameraList = system.GetCameras();

            var BlackFlys = cameraList.Where(c => c.GetTLDeviceNodeMap().GetNode<IString>("DeviceModelName").Value.Contains("Blackfly S")).ToArray();
            //var BlackFlys1 = cameraList.Where(c => c.GetTLDeviceNodeMap().Values.d.Contains("BlackFly S")).ToArray();
            //var Nodes = cameraList.Select(c => c.GetTLDeviceNodeMap()).ToArray();
            //if (BlackFlys.Length < 1)
            //    return;

            //IManagedCamera cam = BlackFlys[0];

            if(cameraList.Count <1)
            {
                MessageBox.Show("No camera is found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }

            IManagedCamera cam = cameraList[0];
            SpinCamColor = cam;




            // Retrieve TL device nodemap and print device information
            INodeMap nodeMapTLDevice = cam.GetTLDeviceNodeMap();
            // Retrieve device serial number for filename
            String deviceSerialNumber = "";
            IString iDeviceSerialNumber = nodeMapTLDevice.GetNode<IString>("DeviceSerialNumber");
            deviceSerialNumber = iDeviceSerialNumber.Value;

            //Print device info
            ICategory category = nodeMapTLDevice.GetNode<ICategory>("DeviceInformation");
            for (int i = 0; i < category.Children.Length; i++)
            {
                Debug.WriteLine("{0}: {1}", category.Children[i].Name, (category.Children[i].IsReadable ? category.Children[i].ToString() : "Node not available"));
            }

            cam.Init();

            //cam.ExposureTimeMode.Value = cam.ExposureTimeMode.Symbolics[0];
            // Retrieve GenICam nodemap
            INodeMap nodeMap = cam.GetNodeMap();
            // Configure custom image settings
            //ConfigureCustomImageSettings(nodeMap);
            // Apply mono 8 pixel format
            IEnum iPixelFormat = nodeMap.GetNode<IEnum>("PixelFormat");
            IEnumEntry iPixelFormatMono8 = iPixelFormat.GetEntryByName("Mono8");
            //iPixelFormat.Value = iPixelFormatMono8.Value;
            // Apply minimum to offset X
            IInteger iOffsetX = nodeMap.GetNode<IInteger>("OffsetX");
            iOffsetX.Value = iOffsetX.Min;

            IEnum iAcquisitionMode = nodeMap.GetNode<IEnum>("AcquisitionMode");
            IEnumEntry iAcquisitionModeContinuous = iAcquisitionMode.GetEntryByName("Continuous");
            iAcquisitionMode.Value = iAcquisitionModeContinuous.Symbolic;
            // Begin acquiring images
            cam.BeginAcquisition();

            using (IManagedImage rawImage = SpinCamColor.GetNextImage())
            {
                if (!rawImage.IsIncomplete)
                {
                    using (IManagedImage RawConvertedImage = rawImage.Convert(PixelFormatEnums.BGRa8))
                    {
                        rawImage.ConvertToBitmapSource(PixelFormatEnums.BGR8, RawConvertedImage, ColorProcessingAlgorithm.DEFAULT);
                        convertedImage = RawConvertedImage.bitmapsource.Clone();
                        i++;
                        App.Current.Dispatcher.Invoke(new Action(() => { RefreshScreen(); }), DispatcherPriority.Send);
                        this.Title = SpinCamColor.ExposureTime.ToString() + SpinCamColor.DeviceSerialNumber.ToString() + cam.DeviceModelName;
                    }
                }
            }

            cam.EndAcquisition();
            //gridControl.Connect(cam);
            //cam.DeInit();
            //system.Dispose();
            RefreshThread = new Thread(GetImages);
            RefreshThread.Start();
        }


        bool Refreshing = false;
        int i = 0;
        long LastImageSum = 0;
        public BitmapSource convertedImage = null;
        public BitmapSource PrevConvertedImage = null;
        public long PrevImageSum = 0;
        private void GetImages()
        {
            for (;;)
                if (Refreshing) 
                {
                    try
                    {
                        using (IManagedImage rawImage = SpinCamColor.GetNextImage())
                        {
                            if (!rawImage.IsIncomplete)
                            {
                                try
                                {
                                        CC.Dispatcher.Invoke(new Action(() =>
                                        { 
                                            rawImage.ConvertToBitmapSource(PixelFormatEnums.BGR8, rawImage, ColorProcessingAlgorithm.DEFAULT);
                                            PrevConvertedImage = convertedImage;
                                            convertedImage = rawImage.bitmapsource;
                                            i++;
                                            PrevImageSum = LastImageSum;
                                            LastImageSum = FindSum(convertedImage);

                                            RefreshScreen();
                                        }), DispatcherPriority.Normal);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(i.ToString() + " " + ex.Message + "\n" + ex.StackTrace.ToString());
                                }
                            }
                            if (i % 200 == 0)
                                GC.Collect();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("GetFrameError: " + ex.Message);
                    }
                }
        }

        void RefreshScreen()
        {
            if (convertedImage == null)
                return;
            if (!(bool)DrawDiffCheckBox.IsChecked)
            {
                CC.Source = convertedImage;

            }
            else
            {
                CC.Source = FindColoredDifference(convertedImage, PrevConvertedImage);
            }

            Title = "STROBE II: Reads count:" + i.ToString() + " last sum:" + LastImageSum.ToString();
        }

        unsafe public long FindSum(BitmapSource bs)
        {
            if (bs == null)
                return -1;

            WriteableBitmap wb = new WriteableBitmap(bs);
            wb.Lock();
            byte* bb = (byte *)wb.BackBuffer.ToPointer();
            long Sum = 0;
            long L = (int)wb.Width * (int)wb.Height * 3;
            for(int i=0; i < L; i+=3)
            //for (int x = 0, i = 0; x < wb.Width; x++)
            //    for (int y = 0; y < wb.Height; y++)
                    Sum += bb[i];
            wb.Unlock();
            return Sum;
        }

        unsafe public WriteableBitmap FindColoredDifference(BitmapSource bs1, BitmapSource bs2)
        {
            if (bs1 == null)
                return null;
            
            WriteableBitmap wb1 = new WriteableBitmap(bs1);
            WriteableBitmap wb2 = new WriteableBitmap(bs2);
            WriteableBitmap wb;
            if (LastImageSum <PrevImageSum)
                wb = new WriteableBitmap(bs1);
            else
                wb = new WriteableBitmap(bs2);

            wb1.Lock(); wb2.Lock(); wb.Lock();
            byte* bb1 = (byte*)wb1.BackBuffer.ToPointer();
            byte* bb2 = (byte*)wb2.BackBuffer.ToPointer();
            byte* bb = (byte*)wb.BackBuffer.ToPointer();

            int dif = 0;
            int temp;
            byte res = 0;
            bool GreenFlu = (bool)radioButtonGreen.IsChecked;
            int amp = (int)(AmplificationSlider.Value);
            long L = (int)wb1.Width * (int)wb1.Height * 3;
            bool Grayed = (bool)checkBoxGray.IsChecked;
            for (int b = 0, g = 1, r = 2; b < L; b += 3, r += 3, g += 3)
            {
                if (GreenFlu)
                    dif = (bb1[g] - bb2[g]);
                else
                    dif = (bb1[r] - bb2[r]);
                if (dif < 0)
                    dif = -dif;

                res = bb[g];
                if (amp > 0)
                    dif <<= amp;
                if (amp < 0)
                    dif >>= -amp;
                //if (bb[r] + dif > 255) bb[r] = 255; else bb[r] += (byte)dif;
                temp = res + dif;
                if (temp > 255)
                    bb[g] = 255;
                else
                    bb[g] = (byte)(temp);

                if (Grayed)
                    bb[b] = res; bb[r] = res;
            }

            wb.Unlock(); wb1.Unlock(); wb2.Unlock();
            return wb;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (SpinCamColor == null)
                return;
            try
            {
                SpinCamColor.BeginAcquisition();
                Refreshing = true;
            }
            catch { }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (SpinCamColor == null)
                return;
            Refreshing = false;
            try
            {
                SpinCamColor.EndAcquisition();
            }
            catch { }
        }

        PropertyGridWindow prop = null;
        GUIFactory AcquisitionGUI = null;
        private void button2_Click(object sender, RoutedEventArgs e)
        {
            if (AcquisitionGUI == null)
            {
                AcquisitionGUI = new GUIFactory();
                AcquisitionGUI.ConnectGUILibrary(SpinCamColor);
            }
            if (this.prop == null)
            {
                prop = AcquisitionGUI.GetPropertyGridWindow();
                prop.Connect(SpinCamColor);
            }
            prop.ShowModal();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (SpinCamColor != null)
                if (SpinCamColor.IsStreaming())
                    SpinCamColor.EndAcquisition();

            if (RefreshThread.IsAlive)
                RefreshThread.Abort();
            e.Cancel = false;
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SpinCamColor.EndAcquisition();
            }
            catch { }
            try
            {

                RefreshThread.Abort();
            }
            catch { }
            Application.Current.Shutdown();
        }

        private void button4_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)CC.Source));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG", 
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond, 
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo " + ((bool)radioButtonGreen.IsChecked ? "green" : "red") + "_Coef" + (int)(AmplificationSlider.Value)) 
                    );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
