namespace WarbandToBannerlordConverter;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string j = args.Length > 0 ? args.FirstOrDefault(f => f.EndsWith(".json")) : "";
        string x = args.Length > 0 ? args.FirstOrDefault(f => f.EndsWith(".xscene")) : "";

        Application.Run(new MainForm(j, x));
    }
}