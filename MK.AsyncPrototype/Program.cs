using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;


AsyncLocal<int> localI = new();
var tasks = new List<MyTask>();

//for (int i = 0; i < 100; i++)
//{
//    localI.Value = i;
//    //MyThreadPool.QueueUserWorkItem(() =>
//    //{
//    //    Console.WriteLine(localI.Value);
//    //    Thread.Sleep(1000);
//    //});

//    tasks.Add(MyTask.Run(() =>
//    {
//        Console.WriteLine(localI.Value);
//        Thread.Sleep(1000);
//    }));
//}
//Console.ReadLine();
//tasks.ForEach(t => t.Wait());

//Console.Write("Hello, ");
//MyTask.Delay(TimeSpan.FromSeconds(1)).ContinueWith(() =>
//{
//    Console.Write("Warsaw ");
//    return MyTask.Delay(TimeSpan.FromSeconds(1)).ContinueWith(() =>
//    {
//        Console.Write("Thank you ");
//        return MyTask.Delay(TimeSpan.FromSeconds(1)).ContinueWith(() =>
//        {
//            Console.Write("For ");
//        });
//    });
//}).Wait();

SayHello().Wait();

//static IEnumerable<MyTask> SayHello()
//{
//    while (true)
//    {
//        Console.Write("Hello, ");
//        yield return MyTask.Delay(TimeSpan.FromSeconds(1));
//        Console.Write("Warsaw ");
//        yield return MyTask.Delay(TimeSpan.FromSeconds(1));
//        Console.Write("Thank you ");
//        yield return MyTask.Delay(TimeSpan.FromSeconds(1));
//        Console.Write("For ");
//    }
//}

static async MyTask SayHello()
{
    while (true)
    {
        Console.Write("Hello, ");
        await MyTask.Delay(TimeSpan.FromSeconds(1));
        Console.Write("Warsaw ");
        await MyTask.Delay(TimeSpan.FromSeconds(1));
        Console.Write("Thank you ");
        await MyTask.Delay(TimeSpan.FromSeconds(1));
        Console.Write("For having me! ");
    }
}


class MyTaskAsyncMethodBuilder
{
    public static MyTaskAsyncMethodBuilder Create() => new();
    public MyTask Task { get; } = new();

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine

    {
        ExecutionContext? ec = ExecutionContext.Capture();
        try
        {
            stateMachine.MoveNext();
        }
        finally 
        {
            if (ec is not null)
            {
                ExecutionContext.Restore(ec);
            }
        }
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    public void SetResult() => Task.SetResult();
    public void SetException(Exception e) => Task.SetException(e);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
        where TAwaiter : INotifyCompletion =>
        awaiter.OnCompleted(stateMachine.MoveNext);

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
    ref TAwaiter awaiter,
    ref TStateMachine stateMachine)
    where TStateMachine : IAsyncStateMachine
    where TAwaiter : INotifyCompletion =>
    awaiter.OnCompleted(stateMachine.MoveNext);
}

[AsyncMethodBuilder(typeof(MyTaskAsyncMethodBuilder))]
class MyTask
{
    private bool _completed;
    private Exception? _exception;
    private Action? _continuation;
    private ExecutionContext? _ec;

    public struct Awaiter(MyTask task) : INotifyCompletion
    {
        public bool IsCompleted => task.IsCompleated;
        public void GetResult() => task.Wait();
        public void OnCompleted(Action action) => task.ContinueWith(action);
    }

    public Awaiter GetAwaiter() => new(this);

    public bool IsCompleated
    {
        get
        {
            lock (this)
            {
                return _completed;
            }
        }
    }

    public void Wait()
    {
        ManualResetEventSlim? mres = null;
        lock (this)
        {
            if (!_completed)
            {
                mres = new ManualResetEventSlim();
                ContinueWith(mres.Set);

            }
        }
        mres?.Wait();
        if (_exception is not null)
        {
            //throw _exception;
            //throw new AggregateException(_exception);
            ExceptionDispatchInfo.Throw(_exception);
        }
    }

    public MyTask ContinueWith(Func<MyTask> action)
    {
        var task = new MyTask();
        Action continuation = () =>
        {
            try
            {
                MyTask newTask = action();
                newTask.ContinueWith(() =>
                {
                    if (newTask._exception is not null)
                    {
                        task.SetException(newTask._exception);
                    }
                    else
                    {
                        task.SetResult();
                    }
                });
            }
            catch (Exception ex)
            {
                task.SetException(ex);
                return;
            }
        };

        lock (this)
        {
            if (_completed)
            {
                MyThreadPool.QueueUserWorkItem(continuation);
            }
            else
            {
                if (_continuation is not null)
                {
                    throw new InvalidOperationException("This is not the Task you're looking for.");
                }

                _continuation = continuation;
                _ec = ExecutionContext.Capture();
            }
        }
        return task;
    }

    public MyTask ContinueWith(Action action)
    {
        var task = new MyTask();
        Action continuation = () =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                task.SetException(ex);
                return;
            }
            task.SetResult();
        };

        lock (this)
        {
            if (_completed)
            {
                MyThreadPool.QueueUserWorkItem(continuation);
            }
            else
            {
                if (_continuation is not null)
                {
                    throw new InvalidOperationException("This is not the Task you're looking for.");
                }

                _continuation = continuation;
                _ec = ExecutionContext.Capture();
            }
        }
        return task;
    }

    public void SetResult() => Complete(null);

    public void SetException(Exception exception) => Complete(exception);

    private void Complete(Exception? exception)
    {
        lock (this)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Stop messsing up the demo");
            }
            _completed = true;
            _exception = exception;


            if (_continuation is not null)
            {
                MyThreadPool.QueueUserWorkItem(() =>
                {
                    if (_ec is null)
                    {
                        _continuation();
                    }
                    else
                    {
                        ExecutionContext.Run(_ec, _ => _continuation(), null);
                    }
                });
            }
        }
    }
    public static MyTask Run(Action action)
    {
        var task = new MyTask();
        MyThreadPool.QueueUserWorkItem(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                task.SetException(ex);
                return;
            }
            task.SetResult();
        });
        return task;
    }

    public static MyTask Delay(TimeSpan delay)
    {
        var task = new MyTask();
        new Timer(_ => task.SetResult()).Change(delay, Timeout.InfiniteTimeSpan);
        return task;
    }

    public static MyTask WhenAll(MyTask task1, MyTask task2)
    {
        var task = new MyTask();
        int remaining = 2;
        Action continuation = () =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                task.SetResult();
            }
        };

        task1.ContinueWith(continuation);
        task2.ContinueWith(continuation);
        return task;
    }

    public static MyTask Iterate(IEnumerable<MyTask> tasks)
    {
        var task = new MyTask();

        IEnumerator<MyTask> e = tasks.GetEnumerator();

        void MoveNext()
        {
            try
            {
                if (e.MoveNext())
                {
                    MyTask nexttask = e.Current;
                    nexttask.ContinueWith(MoveNext);
                    return;
                }
            }
            catch (Exception ex)
            {
                task.SetException(ex);
                return;
            }
            task.SetResult();
        }
        MoveNext();
        return task;
    }
}

/// <summary>
/// Custom Thread pull
/// </summary>
static class MyThreadPool
{
    private static readonly BlockingCollection<(Action, ExecutionContext?)> s_workItems = new();

    public static void QueueUserWorkItem(Action action) =>
        s_workItems.Add((action, ExecutionContext.Capture()));

    static MyThreadPool()
    {
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            new Thread(() =>
            {
                while (true)
                {
                    (Action action, ExecutionContext? ec) = s_workItems.Take();
                    if (ec is null)
                    {
                        action();
                    }
                    else
                    {
                        ExecutionContext.Run(ec, _ => action(), null);
                    }
                }

            })
            { IsBackground = true }.Start();
        }
    }
}