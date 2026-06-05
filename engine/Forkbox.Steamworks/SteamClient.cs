using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Forkbox.Steamworks;

/// <summary>
/// Minimal Steam client bootstrap for the Forkbox Steamworks wrapper.
/// </summary>
public static class SteamClient
{
	private const uint DefaultAppId = 590830;
	private static readonly object InitLock = new();
	private static bool resolverRegistered;
	private static bool engineLoggerResolved;
	private static object engineLogger;
	private static MethodInfo engineLoggerInfo;
	private static IntPtr nativeSteamApiHandle;
	private static bool nativeSteamApiLogged;

	/// <summary>
	/// Initialize the bundled Facepunch Steamworks client if it hasn't already been initialized.
	/// </summary>
	public static void Init( uint appId = DefaultAppId, bool asyncCallbacks = true )
	{
		EnsureNativeResolver();
		LogFacepunchAssembly( "init requested" );
		LogLoadedSteamApiModules();

		var wasValid = global::Steamworks.SteamClient.IsValid;
		Log( $"SteamClient.IsValid before init: {wasValid}" );

		if ( global::Steamworks.SteamClient.IsValid )
			return;

		lock ( InitLock )
		{
			if ( global::Steamworks.SteamClient.IsValid )
				return;

			try
			{
				Log( $"Calling SteamClient.Init appId={appId} asyncCallbacks={asyncCallbacks}" );
				global::Steamworks.SteamClient.Init( appId, asyncCallbacks );
				var isValid = global::Steamworks.SteamClient.IsValid;
				Log( $"SteamClient.Init succeeded. IsValid={isValid}" );
			}
			catch ( Exception ex )
			{
				Log( $"SteamClient.Init failed: {ex}" );
				if ( IsNonFatalAuthOnlyInitFailure( ex ) )
				{
					Log( "Continuing with auth-only SteamUser path after non-fatal SteamClient.Init failure." );
					return;
				}

				throw;
			}
		}
	}

	/// <summary>
	/// True if the bundled Facepunch Steamworks client is initialized.
	/// </summary>
	public static bool IsValid => global::Steamworks.SteamClient.IsValid;

	/// <summary>
	/// Pump callbacks when initialized with asyncCallbacks set to false.
	/// </summary>
	public static void RunCallbacks()
	{
		if ( global::Steamworks.SteamClient.IsValid )
			global::Steamworks.SteamClient.RunCallbacks();
	}

	/// <summary>
	/// Shutdown the bundled Facepunch Steamworks client.
	/// </summary>
	public static void Shutdown()
	{
		if ( global::Steamworks.SteamClient.IsValid )
			global::Steamworks.SteamClient.Shutdown();
	}

	internal static void LogFacepunchAssembly( string context )
	{
		var assembly = typeof( global::Steamworks.SteamClient ).Assembly;
		Log( $"Facepunch.Steamworks assembly ({context}): {assembly.FullName}" );
		Log( $"Facepunch.Steamworks location ({context}): {assembly.Location}" );
	}

	private static void EnsureNativeResolver()
	{
		if ( resolverRegistered )
			return;

		lock ( InitLock )
		{
			if ( resolverRegistered )
				return;

			var assembly = typeof( global::Steamworks.SteamClient ).Assembly;

			try
			{
				NativeLibrary.SetDllImportResolver( assembly, ResolveFacepunchNativeLibrary );
				Log( $"Registered native resolver for {assembly.GetName().Name}" );
			}
			catch ( InvalidOperationException ex )
			{
				Log( $"Native resolver was already registered for {assembly.GetName().Name}: {ex.Message}" );
			}

			resolverRegistered = true;
		}
	}

	private static IntPtr ResolveFacepunchNativeLibrary( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		if ( !string.Equals( libraryName, "steam_api64", StringComparison.OrdinalIgnoreCase ) )
			return IntPtr.Zero;

		if ( nativeSteamApiHandle != IntPtr.Zero )
			return nativeSteamApiHandle;

		var nativePath = Path.Combine( GetThirdPartyPath(), "steam_api64.dll" );
		LogOnce( $"Resolving Facepunch native '{libraryName}' from: {nativePath}" );

		if ( !File.Exists( nativePath ) )
		{
			var message = $"Forkbox native steam_api64.dll missing at {nativePath}. Add the steam_api64.dll that matches Facepunch.Steamworks.Win64.dll here.";
			Log( message );
			throw new DllNotFoundException( message );
		}

		try
		{
			nativeSteamApiHandle = NativeLibrary.Load( nativePath, assembly, searchPath );
			LogOnce( $"Loaded Forkbox native steam_api64.dll: {nativePath}" );
			LogNativeSteamApiExports( nativeSteamApiHandle );
			return nativeSteamApiHandle;
		}
		catch ( Exception ex )
		{
			Log( $"Failed to load Forkbox native steam_api64.dll from {nativePath}: {ex}" );
			throw new DllNotFoundException( $"Failed to load Forkbox native steam_api64.dll from {nativePath}", ex );
		}
	}

	private static string GetThirdPartyPath()
	{
		var assemblyPath = typeof( SteamClient ).Assembly.Location;
		var assemblyDirectory = Path.GetDirectoryName( assemblyPath );
		if ( !string.IsNullOrWhiteSpace( assemblyDirectory ) )
			return assemblyDirectory;

		return Path.Combine( AppContext.BaseDirectory, "bin", "thirdparty" );
	}

	private static void LogLoadedSteamApiModules()
	{
		try
		{
			foreach ( ProcessModule module in Process.GetCurrentProcess().Modules )
			{
				if ( string.Equals( module.ModuleName, "steam_api64.dll", StringComparison.OrdinalIgnoreCase ) )
					Log( $"Already loaded steam_api64.dll: {module.FileName}" );
			}
		}
		catch ( Exception ex )
		{
			Log( $"Unable to inspect loaded steam_api64.dll modules: {ex.Message}" );
		}
	}

	private static bool IsNonFatalAuthOnlyInitFailure( Exception ex )
	{
		for ( var current = ex; current is not null; current = current.InnerException )
		{
			if ( current is EntryPointNotFoundException && current.Message.Contains( "SteamAPI_SteamApps_v008", StringComparison.OrdinalIgnoreCase ) )
				return true;
		}

		return false;
	}

	internal static void Log( string message )
	{
		var line = $"[Forkbox.Steamworks] {message}";
		LogToEngine( line );
		Debug.WriteLine( line );
		Console.WriteLine( line );
	}

	private static void LogOnce( string message )
	{
		if ( nativeSteamApiLogged )
			return;

		Log( message );
	}

	private static void LogNativeSteamApiExports( IntPtr handle )
	{
		if ( nativeSteamApiLogged )
			return;

		nativeSteamApiLogged = true;

		Log( $"steam_api64 export SteamAPI_SteamFriends_v018: {NativeLibrary.TryGetExport( handle, "SteamAPI_SteamFriends_v018", out _ )}" );
		Log( $"steam_api64 export SteamAPI_SteamUser_v023: {NativeLibrary.TryGetExport( handle, "SteamAPI_SteamUser_v023", out _ )}" );
		Log( $"steam_api64 export SteamAPI_SteamApps_v008: {NativeLibrary.TryGetExport( handle, "SteamAPI_SteamApps_v008", out _ )}" );
	}

	private static void LogToEngine( string line )
	{
		try
		{
			if ( !engineLoggerResolved )
			{
				engineLoggerResolved = true;
				foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
				{
					var loggerType = assembly.GetType( "Sandbox.Diagnostics.Logger", false );
					if ( loggerType is null )
						continue;

					engineLogger = Activator.CreateInstance( loggerType, "Forkbox.Steamworks" );
					engineLoggerInfo = loggerType.GetMethod( "Info", [typeof( object )] );
					break;
				}
			}

			engineLoggerInfo?.Invoke( engineLogger, [line] );
		}
		catch
		{
			// Logging must never change Steamworks behavior.
		}
	}
}
