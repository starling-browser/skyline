// SPDX-License-Identifier: Apache-2.0
using Foundation;
using UIKit;

namespace StarlingMock;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new BrowserViewController(),
        };
        Window.MakeKeyAndVisible();
        return true;
    }
}
