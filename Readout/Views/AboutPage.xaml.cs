namespace Readout.Views;

public partial class AboutPage : ContentPage
{
    public const string SourceUrl = "https://github.com/amd989/readout";

    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {Services.AppVersion.Text}";
        LicenseLabel.Text = MitLicense;
    }

    private async void OnSourceClicked(object? sender, EventArgs e) =>
        await Launcher.Default.OpenAsync(new Uri(SourceUrl));

    private const string MitLicense = """
        MIT License

        Copyright (c) 2026 Alejandro Mora

        Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

        Portions are based on the EEVblog 121GW app, Copyright (c) 2018 David Ledger, under the same license.
        """;
}
