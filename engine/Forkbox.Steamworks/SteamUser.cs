using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Forkbox.Steamworks;

/// <summary>
/// Minimal Steam user auth wrapper for standalone fork builds.
/// </summary>
public static class SteamUser
{
	private static readonly Lazy<EngineGlueBridge> EngineGlue = new( EngineGlueBridge.Create );

	/// <summary>
	/// Gets a Steam auth token for the named service.
	/// </summary>
	public static AuthToken GetAuthToken( string serviceName )
	{
		EngineGlue.Value.RequestWebAuthTicket();
		for ( var i = 0; i < 10000 / 100; i++ )
		{
			var token = EngineGlue.Value.GetWebAuthTicket();
			if ( token is not null )
				return CreateAuthToken( token );

			Thread.Sleep( 100 );
		}

		EngineGlue.Value.CancelWebAuthTicket();
		return null;
	}

	/// <summary>
	/// Gets a Steam auth token for the named service and waits for Steam to confirm the ticket is ready.
	/// </summary>
	public static async Task<AuthToken> GetAuthTokenAsync( string serviceName, double timeoutSeconds = 10.0 )
	{
		EngineGlue.Value.RequestWebAuthTicket();

		var timeout = TimeSpan.FromSeconds( timeoutSeconds );
		var started = DateTime.UtcNow;
		while ( DateTime.UtcNow - started < timeout )
		{
			var token = EngineGlue.Value.GetWebAuthTicket();
			if ( token is not null )
				return CreateAuthToken( token );

			await Task.Delay( 100 );
		}

		EngineGlue.Value.CancelWebAuthTicket();
		return null;
	}

	private static AuthToken CreateAuthToken( string token )
	{
		return new AuthToken( token, EngineGlue.Value.CancelWebAuthTicket );
	}

	private sealed class EngineGlueBridge
	{
		private readonly MethodInfo requestWebAuthTicket;
		private readonly MethodInfo getWebAuthTicket;
		private readonly MethodInfo cancelWebAuthTicket;

		private EngineGlueBridge( Type engineGlueType )
		{
			const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

			requestWebAuthTicket = engineGlueType.GetMethod( "RequestWebAuthTicket", flags )
				?? throw new MissingMethodException( engineGlueType.FullName, "RequestWebAuthTicket" );
			getWebAuthTicket = engineGlueType.GetMethod( "GetWebAuthTicket", flags )
				?? throw new MissingMethodException( engineGlueType.FullName, "GetWebAuthTicket" );
			cancelWebAuthTicket = engineGlueType.GetMethod( "CancelWebAuthTicket", flags )
				?? throw new MissingMethodException( engineGlueType.FullName, "CancelWebAuthTicket" );
		}

		public static EngineGlueBridge Create()
		{
			foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
			{
				var engineGlueType = assembly.GetType( "NativeEngine.EngineGlue", false );
				if ( engineGlueType is not null )
					return new EngineGlueBridge( engineGlueType );
			}

			throw new InvalidOperationException( "NativeEngine.EngineGlue is not available. Forkbox.Steamworks can only request Steam auth tokens after the engine has initialized." );
		}

		public void RequestWebAuthTicket()
		{
			requestWebAuthTicket.Invoke( null, null );
		}

		public string GetWebAuthTicket()
		{
			return (string)getWebAuthTicket.Invoke( null, null );
		}

		public void CancelWebAuthTicket()
		{
			cancelWebAuthTicket.Invoke( null, null );
		}
	}
}
