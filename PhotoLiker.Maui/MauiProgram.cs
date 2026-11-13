using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace PhotoLiker.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// A smoke tour must never touch the real %LocalAppData%\PhotoLiker: redirect before anything reads AppPaths
		SmokeTour.SmokeTourOptions.RedirectAppData(Environment.GetCommandLineArgs());

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
