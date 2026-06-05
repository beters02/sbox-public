using System;
using System.Threading.Tasks;

namespace Forkbox.Steamworks;

/// <summary>
/// Minimal Steam user auth wrapper for standalone fork builds.
/// </summary>
public static class SteamUser
{
	/// <summary>
	/// Gets a Steam auth token for the named service.
	/// </summary>
	public static AuthToken GetAuthToken( string serviceName )
	{
#pragma warning disable CA2000 // Ownership is transferred to the caller.
		return GetAuthTokenAsync( serviceName ).GetAwaiter().GetResult();
#pragma warning restore CA2000
	}

	/// <summary>
	/// Gets a Steam auth token for the named service and waits for Steam to confirm the ticket is ready.
	/// </summary>
	public static async Task<AuthToken> GetAuthTokenAsync( string serviceName, double timeoutSeconds = 10.0 )
	{
		SteamClient.Init();
		SteamClient.LogFacepunchAssembly( "auth ticket requested" );
		SteamClient.Log( $"Requesting auth ticket for service '{serviceName}' timeout={timeoutSeconds}" );

		var ticket = await global::Steamworks.SteamUser.GetAuthTicketForWebApiAsync( serviceName, timeoutSeconds ).ConfigureAwait( false );
		SteamClient.Log( $"Auth ticket result: {(ticket is null ? "null" : $"{ticket.Data?.Length ?? 0} bytes")}" );

		return CreateAuthToken( ticket );
	}

	private static AuthToken CreateAuthToken( global::Steamworks.AuthTicket ticket )
	{
		if ( ticket is null )
			return null;

		return new AuthToken( Convert.ToHexString( ticket.Data ), ticket.Cancel );
	}
}
