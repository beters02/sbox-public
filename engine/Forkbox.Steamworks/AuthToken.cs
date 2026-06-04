using System;
using System.Text;

namespace Forkbox.Steamworks;

/// <summary>
/// A Steam auth token returned by <see cref="SteamUser.GetAuthToken"/>.
/// </summary>
public sealed class AuthToken : IDisposable
{
	private readonly Action cancel;
	private bool canceled;

	internal AuthToken( string token, Action cancel )
	{
		Value = token ?? throw new ArgumentNullException( nameof( token ) );
		this.cancel = cancel ?? throw new ArgumentNullException( nameof( cancel ) );

		Data = ParseTokenData( Value );
		Hex = Convert.ToHexString( Data );
	}

	/// <summary>
	/// Steam Web API auth ticket string returned by the engine.
	/// </summary>
	public string Value { get; }

	/// <summary>
	/// Raw ticket bytes that should be sent to the service that will authenticate the Steam user.
	/// </summary>
	public byte[] Data { get; }

	/// <summary>
	/// Uppercase hexadecimal representation of <see cref="Data"/>.
	/// </summary>
	public string Hex { get; }

	/// <summary>
	/// Cancel the underlying Steam auth ticket.
	/// </summary>
	public void Cancel()
	{
		if ( canceled )
			return;

		canceled = true;
		cancel();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		Cancel();
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Value;
	}

	private static byte[] ParseTokenData( string token )
	{
		try
		{
			return Convert.FromHexString( token );
		}
		catch ( FormatException )
		{
			return Encoding.UTF8.GetBytes( token );
		}
	}
}
