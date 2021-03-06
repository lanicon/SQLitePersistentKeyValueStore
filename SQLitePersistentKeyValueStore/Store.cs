using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;

namespace SQLitePersistentKeyValueStore
{
    /// <summary>
    /// Keys are stored as TEXT, Values are stored as binary blobs.
    /// 
    /// Current speed ups are possible - see akavache optimizations.
    /// We should also do something about the task array, probably not very memory efficient
    /// </summary>
    public class Store : IDisposable
    {
        public string DatabasePath { get; private set; }
        public string Table { get; private set; }

        private string getConnectionString(string databasePath = null)
            => $"Data Source={databasePath ?? DatabasePath};Version=3;";

        private ConcurrentDictionary<string, byte[]> cache = new ConcurrentDictionary<string, byte[]>();

        private ConcurrentQueue<string> persistenceQueue = new ConcurrentQueue<string>();

        List<Task> writerTasks = new List<Task>();

        private void persist(string key, byte[] value)
        {
            persistenceQueue.Enqueue(key);
            writerTasks.Add(Task.Run(() => writer()));
        }

        private void writer()
        {

            lock (persistenceQueue)
            {

                using (var con = new SQLiteConnection(getConnectionString()))
                {

                    con.Open();

                    SQLiteCommand InsertCommand = new SQLiteCommand(con);
                    InsertCommand.CommandText = $"INSERT OR REPLACE INTO {Table} (key, value) VALUES (@key, @value)";
                    InsertCommand.Prepare();

                    SQLiteCommand DeleteCommand = new SQLiteCommand(con);
                    DeleteCommand.CommandText = $"DELETE FROM {Table} WHERE key = @key";
                    DeleteCommand.Prepare();

                    using (SQLiteTransaction tr = con.BeginTransaction())
                    {

                        InsertCommand.Transaction = tr;
                        DeleteCommand.Transaction = tr;

                        while (persistenceQueue.TryDequeue(out string key))
                        {
                            if (!cache.ContainsKey(key))
                            {
                                DeleteCommand.Parameters.AddWithValue("@key", key);
                                DeleteCommand.ExecuteNonQuery();
                            }
                            else
                            {
                                InsertCommand.Parameters.AddWithValue("@key", key);
                                InsertCommand.Parameters.AddWithValue("@value", cache[key]);
                                InsertCommand.ExecuteNonQuery();
                            }
                        }

                        tr.Commit();

                        SQLiteCommand VacuumCommand = new SQLiteCommand("VACUUM", con);
                        VacuumCommand.ExecuteNonQuery();

                    }

                    con.Close();

                }

            }

        }

        public void Put(string key, byte[] value)
        {
            cache[key] = value;
            persist(key, value);
        }

        public void Delete(string key)
        {
            if (!cache.TryRemove(key, out _))
                throw new KeyNotFoundException(key);
            else
                persist(key, null);
        }

        private byte[] getFromDisk(string key)
        {

            var SelectCommand = new SQLiteCommand();
            SelectCommand.CommandText = $"SELECT value FROM {Table} WHERE key = @key";
            SelectCommand.Prepare();

            using (var con = new SQLiteConnection(getConnectionString()))
            {
                con.Open();

                SelectCommand.Connection = con;
                SelectCommand.Parameters.AddWithValue("@key", key);
                SelectCommand.ExecuteNonQuery();

                using (SQLiteDataReader reader = SelectCommand.ExecuteReader())
                {
                    reader.Read();
                    try
                    {
                        var value = reader["value"] as byte[];
                        cache[key] = value;
                        return value;
                    }
                    catch (InvalidOperationException)
                    {
                        throw new KeyNotFoundException(key);
                    }
                }
            }

        }

        public byte[] Get(string key)
        {
            if (cache.TryGetValue(key, out byte[] value))
                return value;
            else
                return getFromDisk(key);
        }

        private void EnsureDatabaseCreated()
        {

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));

            using (var con = new SQLiteConnection(getConnectionString()))
            {
                con.Open();
                var CreateCommand = new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {Table} (key TEXT PRIMARY KEY NOT NULL, value BLOB)", con);
                CreateCommand.ExecuteNonQuery();
            }

        }

        public byte[] Backup()
        {
            var tempFile = Path.GetTempFileName();

            using (var source = new SQLiteConnection(getConnectionString()))
            using (var destination = new SQLiteConnection(getConnectionString(tempFile)))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination, "main", "main", -1, null, 0);
            }

            var fileAsBytes = File.ReadAllBytes(tempFile);

            File.Delete(tempFile);

            return fileAsBytes;
        }

        public void Restore(byte[] backupFile)
        {

            var tempFile = Path.GetTempFileName();
            File.WriteAllBytes(tempFile, backupFile);

            lock (persistenceQueue)
            {

                // Because of the nature of this operation, it makes sense to delete any current values that are waiting for persistence, or have already been cached.
                cache.Clear();
                while (persistenceQueue.TryDequeue(out _))
                {
                    // Clear the queue
                }

                using (var source = new SQLiteConnection(getConnectionString(tempFile)))
                using (var destination = new SQLiteConnection(getConnectionString()))
                {
                    source.Open();
                    destination.Open();
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
                }

            }

            File.Delete(tempFile);

        }

        ~Store()
            => Dispose();

        public void Dispose()
            => Flush();

        public void Flush()
        {
            Task.WaitAll(writerTasks.ToArray());

            // See https://stackoverflow.com/questions/8511901/system-data-sqlite-close-not-releasing-database-file/39036464
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public Store(string databasePath, string tableName = "data")
        {
            DatabasePath = databasePath;
            Table = tableName;
            EnsureDatabaseCreated();
        }

    }
}