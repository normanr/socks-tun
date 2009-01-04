using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using IEnumerator = System.Collections.IEnumerator;

class BlockingQueue<T> : IEnumerable<T>, IDisposable where T : class
{
	private readonly Queue<T> _queue = new Queue<T>();
	private Semaphore _semaphore = new Semaphore(0, int.MaxValue);

	public void Enqueue(T data)
	{
		if (data == null) throw new ArgumentNullException("data");
		lock (_queue) _queue.Enqueue(data);
		_semaphore.Release();
	}

	public T Dequeue()
	{
		_semaphore.WaitOne();
		lock (_queue) return _queue.Dequeue();
	}

	public bool TryDequeue(int timeout, out T result)
	{
		if (!_semaphore.WaitOne(timeout))
		{
			result = null;
			return false;
		}
		lock (_queue) result = _queue.Dequeue();
		return true;
	}

	public int Count
	{
		get
		{
			return _queue.Count;
		}
	}

	void IDisposable.Dispose()
	{
		if (_semaphore != null)
		{
			_semaphore.Close();
			_semaphore = null;
		}
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		while (true) yield return Dequeue();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable<T>)this).GetEnumerator();
	}
}
