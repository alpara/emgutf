// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;
using System.CodeDom.Compiler;

namespace Example.OSX
{
	[Register ("ViewController")]
	partial class ViewController
	{
		[Outlet]
		AppKit.NSImageView mainImageView { get; set; }

		[Outlet]
		AppKit.NSTextField messageLabel { get; set; }

		[Action ("inceptionClicked:")]
		partial void inceptionClicked (Foundation.NSObject sender);

		[Action ("multiboxClicked:")]
		partial void multiboxClicked (Foundation.NSObject sender);

		[Action ("stylizeClicked:")]
		partial void stylizeClicked (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (messageLabel != null) {
				messageLabel.Dispose ();
				messageLabel = null;
			}

			if (mainImageView != null) {
				mainImageView.Dispose ();
				mainImageView = null;
			}
		}
	}
}
