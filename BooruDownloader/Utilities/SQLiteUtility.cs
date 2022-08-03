using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BooruDownloader.Utilities
{
    public static class SQLiteUtility
    {
        public static void TryCreateTable(SqliteConnection connection)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                SqliteCommand command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS posts
( 
    id INTEGER NOT NULL PRIMARY KEY,
    md5 TEXT,
    tag_string TEXT,
    tag_count_general INTEGER,
    file_ext TEXT
);";
                command.ExecuteNonQuery();

                transaction.Commit();
            }
        }

        public static void InsertOrReplace(SqliteConnection connection, IEnumerable<JObject> posts)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                foreach (JObject post in posts)
                {
                    SqliteCommand command = connection.CreateCommand();

                    command.CommandText = $@"
INSERT OR REPLACE INTO posts ({string.Join(',', post.Properties().Select(p => p.Name))}) VALUES
({string.Join(',', post.Properties().Select(p => '@' + p.Name))})";

                    foreach (JProperty property in post.Properties())
                    {
                        object value = null;

                        switch (property.Value.Type)
                        {
                            case JTokenType.Boolean:
                                {
                                    value = property.Value.ToObject<bool>();
                                }
                                break;
                            default:
                                {
                                    value = property.Value.ToString();
                                }
                                break;

                        }
                        command.Parameters.AddWithValue($"@{property.Name}", value);
                    }

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }
    }
}
