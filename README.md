# SQLite Persistent Key Value Store (for C#)

## What problem does it solve?

* Stores a small number of key-value pairs
* that must be human-editable in their persisted form
* and be accessed by multiple threads
* It must be able to be restored and backed up while running on-the-fly

It is not inteded to be
* A database
* A cache
* Any other catch-all persistence solution.

## How to use

I think the best way to use this is to create a settings class that makes use of this store as the backing mechanism.

* There is some level of protection against data corruption (i.e. against human typing).
* The settings are persisted in the background - you can set and forget.

In this example, we have a simple class that stores only one value; the IP Address of some host.
Note that in this example we must force the disposal of the store to ensure all persistence tasks are finished before the program exits.

```c#

using System;
using System.Net;
using System.Text;
using SQLitePersistentKeyValueStore;

namespace ExampleConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {

            var mySettings = new Settings();
            mySettings.HostIPAddress = IPAddress.Parse("10.1.1.1");
            mySettings.Dispose();

        }
    }

    public class Settings : IDisposable
    {

        private Store store;

        public Settings()
        {
            store = new Store(".\\settings.db");
        }

        public void Dispose()
        {
            store.Dispose();
        }

        public IPAddress HostIPAddress
        {
            get => IPAddress.Parse(Encoding.UTF8.GetString(store.Get("host_ip_address")));
            set => store.Put("host_ip_address", Encoding.UTF8.GetBytes(value.ToString()));
        }

    }

}

```
