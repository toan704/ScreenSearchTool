//namespace ScreenSearchTool
//{
//    internal static class Program
//    {
//        /// <summary>
//        /// The main entry point for the application.
//        /// </summary>
//        [STAThread]
//        static void Main()
//        {
//            ApplicationConfiguration.Initialize();

//            string targetFile = Path.Combine(Application.StartupPath, "paddle_inference_c.dll");
//            string zipFile = Path.Combine(Application.StartupPath, "Lib-support_win-x64.zip");


//            if (!File.Exists(targetFile))
//            {
//                // Trường hợp "Đã tải về và cài đặt"
//                SendStats("Đã tải về và cài đặt");
//                Application.Run(new Form2());
//            }

//            if (File.Exists(targetFile))
//            {
//                // Trường hợp "Đang sử dụng"
//                SendStats("Đang sử dụng");
//                Application.Run(new Form1());
//            }

//        }

//        private static readonly HttpClient client = new HttpClient();

//        public static void SendStats(string message)
//        {
//            string userName = Environment.UserName;
//            string machineName = Environment.MachineName;
//            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

//            var values = new Dictionary<string, string>
//    {
//        { "entry.1363621487", userName },      // Tên người dùng
//        { "entry.1913843925", time },          // Thời gian
//        { "entry.438025676", machineName },    // Tên máy
//        { "entry.1366624685", message }        // Thông điệp
//    };

//            var content = new FormUrlEncodedContent(values);
//            var response = client.PostAsync(
//                "https://docs.google.com/forms/d/e/1FAIpQLSfvKZPZXJHDcTOcli0pgmiTEDM5NAIAT3rygTCIqnN9zAfO4Q/formResponse",
//                content).Result;
//        }

//    }

//}
namespace ScreenSearchTool
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}