using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using RestSharp;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VipLeagueWpf
{

    public partial class MainWindow : Window
    {
        private bool isReallyExit = false;
        private readonly IConfiguration config;

        public MainWindow(IConfiguration config)
        {
            this.config = config;

            InitializeComponent();
        }

        //https://stackoverflow.com/questions/2471867/resize-a-wpf-window-but-maintain-proportions/2767239#2767239
        private const double ASPECT = 16.0 / 9;
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo.WidthChanged) this.Width = sizeInfo.NewSize.Height * ASPECT;
            else this.Height = sizeInfo.NewSize.Width / ASPECT;
        }

        public override void OnApplyTemplate()
        {
            var oDep = GetTemplateChild("btnHelp");
            if (oDep != null)
            {
                ((Button)oDep).Click += this.btnHelp_Click;
            }

            base.OnApplyTemplate();
        }

        private void btnHelp_Click(System.Object sender, System.Windows.RoutedEventArgs e)
        {
            //MessageBox.Show("Help", "", MessageBoxButton.OK, MessageBoxImage.Information);
            //Environment.Exit(0);
            isReallyExit = true;
            this.Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!e.Handled && e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                WindowState = WindowState.Minimized;
            }
            //else if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) {

            //}
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!isReallyExit)
            {
                e.Cancel = true; // this will prevent to close
                WindowState = WindowState.Minimized;
            }
        }
        private void Wv2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string countText = e.TryGetWebMessageAsString();

            int iconWidth = 20;
            int iconHeight = 20;

            RenderTargetBitmap bmp = new RenderTargetBitmap(iconWidth, iconHeight, 96, 96, PixelFormats.Default);
            ContentControl root = new ContentControl();

            root.ContentTemplate = (DataTemplate)Resources["OverlayIcon"];
            root.Content = countText == "0" ? null : countText;

            root.Arrange(new Rect(0, 0, iconWidth, iconHeight));

            bmp.Render(root);

            TaskbarItemInfo.Overlay = bmp;
        }

        private async void webView_Initialized(object sender, System.EventArgs e)
        {
            //wow deploying webView2 is quite complicated since it's a native dll requiring multiple platform versions of the same file
            //adding the await here means we'll at least see the exception when it's not being resolved properly!!!
            //https://github.com/MicrosoftEdge/WebView2Feedback/issues/730
            try
            {
                await wv2.EnsureCoreWebView2Async(); //this initializes wv2.CoreWebView2 (i.e. makes it not null)
            }
            catch (Exception ex) {
                MessageBox.Show(ex.Message, "Error initializing embedded web browser", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            wv2.CoreWebView2InitializationCompleted += (object sender, CoreWebView2InitializationCompletedEventArgs e) =>
            {
                //inject javascript function that scrapes the chat page for message count
                //here's a good thread: https://github.com/MicrosoftEdge/WebView2Feedback/issues/253#issuecomment-641577176
                //another good thread: https://blogs.msmvps.com/bsonnino/2021/02/27/using-the-new-webview2-in-a-wpf-app/
                //https://www.fatalerrors.org/a/excerpt-interaction-between-webview2-and-js.html
                wv2.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync( //this fires on frames as well which gives us full power to override spammity spam vs just the main page
                                                                               //wv2.CoreWebView2.DOMContentLoaded += (object sender, CoreWebView2DOMContentLoadedEventArgs e) =>
                                                                               //{
                                                                               //_ = wv2.ExecuteScriptAsync(
                    $@"
                   //window.onload = () => {{
                        var primed = false;
                        var timerHandle = setInterval(()=>{{

                            var bogusVideoPlaceholder = document.querySelector('.vidCont-bg');
                            if (bogusVideoPlaceholder) {{
                                const txt = document.createElement('h1');
                                txt.innerText = 'there is currently no active stream for this link -Beej';
                                txt.style.color = 'white';
                                bogusVideoPlaceholder.before(txt);
                                bogusVideoPlaceholder.remove();
                            }}

                            if (typeof jQuery === 'undefined') return;
                            var elementHits = jQuery([
                                '.btn-danger', //[Stream Here Now] button in various places
                                '.position-absolute.w-100.h-100', //the main 'click to play' video overlay
                                '.col-12.text-primary', //'click play to continue' text at top of stream page
                                '.d-md-block.text-center > .text-center:not(.w-100)', //[HD Live Stream] button over top of chat, etc
                                '.text-center.text-white', //superfluous messages on video page
                                'iframe:not(.embed-responsive-item):not(#iframe_preview)', //bogus iframes for ad content
                            ].join());
                            
                            if (elementHits.length) {{
                                elementHits.remove(); 
                                primed = true; //run until we get a hit...
                            }}
                            //we can't stop watching because even if we hit on one tag, more nav on the same page might uncover more content needing sweeping
                            //else if (primed) clearInterval(timerHandle); //and then clear the timer so we're not unecessarily spinning 

                            //var videoContainer = jQuery('.col-12.col-md-9.text-center');
                            //if (videoContainer.length) {{
                            //    videoContainer.parent().removeClass();
                            //    videoContainer.removeClass();
                            //}}

                        }}, 100);
                    //}}
                    ");
                //};

                wv2.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                wv2.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

                //translate target=_blank links on the main vipleague page to navigating within our main window
                //otherwise block every other request to open new window!
                wv2.CoreWebView2.NewWindowRequested += (object sender, CoreWebView2NewWindowRequestedEventArgs e) =>
                {
                    if (e.Uri.StartsWith("https://www.vipleague.cc")) wv2.Source = new Uri(e.Uri);
                    e.Handled = true;
                };

                //WebResourceResponseReceived isn't that useful currently since there's no built-in reponse content overrides capability yet
                //https://stackoverflow.com/questions/66428585/webview2-is-it-possible-to-prevent-a-cookie-in-a-response-from-being-stored/66432143#66432143
                //BUT, see suggested technique implemented below in WebResourceRequested event...
                //wv2.CoreWebView2.WebResourceResponseReceived += async (object sender, CoreWebView2WebResourceResponseReceivedEventArgs e) =>{};
            };

        }

        private void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (new[]
            {
                "https://www.vipleague.cc/",
                //"https://www.vipleague.cc/jquery.js",
                //"https://www.vipleague.cc/bootstrap.js",
                //"https://www.vipleague.cc/favicon-32x32.png",
                //"https://www.vipleague.cc/yeti.css",
                //"https://www.vipleague.cc/bundle-",
                //"https://www.vipleague.cc/.*-streaming",
                //"https://www.vipleague.cc/loadschdata",
                //"https://www.vipleague.cc/loadchatdata",
                @"https://fonts\.",
                @"https://cdn\.okamata", //football icons
                "https://cdn.tvply.me", ///scripts/embed.min.js", //tvply.me is the main video stream feeds
                @"chatango\.com", //chat stuff
                //@"taboola\.com" //chat stuff
            }.Any(pattern => Regex.IsMatch(e.Request.Uri, pattern))) return;

            //if (e.ResourceContext == CoreWebView2WebResourceContext.Fetch || e.ResourceContext == CoreWebView2WebResourceContext.XmlHttpRequest) return;
            //if (
            //    e.ResourceContext != CoreWebView2WebResourceContext.Image
            //    && e.Request.Uri.Contains("tvply.me")
            ////&& e.Request.Headers.Any(h => h.Key == "Referer" && h.Value.Contains("streaming-link"))
            //)

            //override response content approach, suggested by:
            //https://stackoverflow.com/questions/66428585/webview2-is-it-possible-to-prevent-a-cookie-in-a-response-from-being-stored/66432143#66432143
            //this particular script pull is crucial to loading all the video player runtime
            if (e.Request.Method == "POST" && e.Request.Uri.StartsWith("https://www.tvply.me")) ///sdembed?v=espnhd~espnsd",                
            {
                using (var deferral = e.GetDeferral())
                {
                    var client = new RestClient(e.Request.Uri);
                    client.Timeout = -1;
                    var request = new RestRequest(Method.POST);
                    request.AddHeader("Origin", "https://www.vipleague.cc");
                    request.AddHeader("Referer", "https://www.vipleague.cc");
                    //unnecessary: request.AddHeader("Cookie", "_pshflg=~; tamedy=2");
                    //unnecessary: request.alwAlwaysMultipartFormData = true;

                    //unnecessary: 
                    //request.AddParameter("ptxt", "gt=ESPN&gc=NFL");
                    //var bodycontent = (e.Request.Content as Stream).ToUtf8String().Split("&");
                    //foreach (var bodyParm in bodycontent)
                    //{
                    //    var bodyParmValue = bodyParm.Split("=");
                    //    request.AddParameter(bodyParmValue[0], System.Web.HttpUtility.UrlDecode(bodyParmValue[1]));
                    //}

                    IRestResponse response = client.Execute(request);
                    var content = response.Content;

                    //they hardended their content against hacking a little bit... just enough to be annoying, nothing really that hard to break with a little effort...
                    //basically there's two layers of "eval()"ing obfuscuted strings...
                    //eventually resulting in the final js that drives the video player
                    //inside that final script, they toss in some hard coded "debugger" breakpoints inside of a tight timer...
                    //this puts your chrome debug window in perpetual pause mode which even prevents inspection of the page elements that you want to target for removal!
                    //sooo... this whole clode block does the webrequest manually and then hacks out the debugger statements! hee hee

                    //var content = Regex.Replace(response.Content, @"(su\.js|vdosupreme)", "xxxx");
                    //zzz.replace(/window\\.addEventListener\\(|window\\.attachEvent\\(|setTimeout\\(/g, ""Function.prototype("")
                    content = Regex.Replace(content, @"eval\((.*)\);", @"eval($1.replace(/eval\((.*)\)/, 'var zzz=$$1.replaceAll(""debugger;"",""""); eval( zzz )'));");
                    e.Response = wv2.CoreWebView2.Environment.CreateWebResourceResponse(content.ToStream(), 200, "Ok", null);
                    deferral.Complete();
                    return;
                }
            }

            e.Response = wv2.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not found", null);
        }
    }

    public static class StreamExtenstions
    {
        public static Stream ToStream(this string value)
            => new MemoryStream(Encoding.UTF8.GetBytes(value ?? string.Empty));

        public static string ToUtf8String(this Stream stream)
        {
            byte[] bytes = new byte[stream.Length];
            stream.Read(bytes, 0, (int)stream.Length);
            return Encoding.UTF8.GetString(bytes);
        }

    }
}
