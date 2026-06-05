namespace Sandbox.Utility;

// 1. Be as fast as possible to iterate
// 2. Queue modifications while iterating
// 3. Don't allow duplicate entries
// 4. Fast as possible removal
// 5. Thread concurrency doesn't matter

/// <summary>
/// Wrapper around a <see cref="HashSet{T}"/> that supports items being added / removed
/// during enumeration. Enumerate the set with <see cref="EnumerateLocked"/>.
/// </summary>
internal class HashSetEx<T> : IHotloadManaged
{
	[SuppressNullKeyWarning]
	private readonly HashSet<T> _hashset = new( 16 );
	private readonly List<T> _cachedList = new( 16 );
	private readonly object _lock = new();

	private bool _listInvalid;
	private int _activeEnumerators;

	/// <summary>
	/// Current number of unique items in the set.
	/// </summary>
	public int Count
	{
		get
		{
			lock ( _lock )
			{
				return _hashset.Count;
			}
		}
	}

	/// <summary>
	/// List view of the set. This is only updated when there are no
	/// active enumerators created by <see cref="EnumerateLocked"/>.
	/// </summary>
	public IReadOnlyList<T> List
	{
		get
		{
			lock ( _lock )
			{
				UpdateList();
				return _cachedList;
			}
		}
	}

	/// <summary>
	/// Adds an item to the set, returning true if it wasn't already present.
	/// If any enumerators are active, they won't see this new item yet.
	/// </summary>
	public bool Add( T obj )
	{
		lock ( _lock )
		{
			if ( !_hashset.Add( obj ) ) return false;

			_listInvalid = true;
			return true;
		}
	}

	/// <summary>
	/// Removes an item from the set, returning true if it was present.
	/// If any enumerators are active, they will still see the removed item.
	/// </summary>
	public bool Remove( T obj )
	{
		lock ( _lock )
		{
			if ( !_hashset.Remove( obj ) ) return false;

			_listInvalid = true;
			return true;
		}
	}

	/// <summary>
	/// Determines whether this set contains the given object.
	/// </summary>
	public bool Contains( T obj )
	{
		lock ( _lock )
		{
			return _hashset.Contains( obj );
		}
	}

	/// <summary>
	/// Remove all items from the set. If any enumerators are active, they won't be affected.
	/// </summary>
	public void Clear()
	{
		lock ( _lock )
		{
			if ( _hashset.Count <= 0 ) return;

			_hashset.Clear();
			_listInvalid = true;
		}
	}

	/// <summary>
	/// If any items were added / removed, and there are no active enumerators, synchronize
	/// <see cref="_cachedList"/> with items from <see cref="_hashset"/>.
	/// </summary>
	private void UpdateList()
	{
		if ( !_listInvalid ) return;
		if ( _activeEnumerators > 0 ) return;

		_listInvalid = false;

		_cachedList.Clear();
		_cachedList.AddRange( _hashset );
	}

	/// <summary>
	/// Enumerates the list, increments iterating before and after. When we finished
	/// iterating, and nothing else is iterating, runs deferred actions.
	/// IMPORTANT: Don't expose this IEnumerable to users directly - because they might purposefully not dispose it?
	/// </summary>
	public IEnumerable<T> EnumerateLocked( bool nullChecks = false )
	{
		lock ( _lock )
		{
			try
			{
				UpdateList();

				_activeEnumerators++;

				foreach ( var item in _cachedList )
				{
					if ( nullChecks && item is IValid { IsValid: false } )
					{
						Remove( item );
						continue;
					}

					yield return item;
				}
			}
			finally
			{
				_activeEnumerators--;
				UpdateList();
			}
		}
	}

	// If types are removed during hotload, items of those types are
	// automatically removed from _hashset because it can't contain null items.
	// Therefore, we'll need to rebuild _cachedList too.

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		lock ( _lock )
		{
			_listInvalid = true;
		}
	}

	void IHotloadManaged.Persisted()
	{
		lock ( _lock )
		{
			_listInvalid = true;
		}
	}
}
