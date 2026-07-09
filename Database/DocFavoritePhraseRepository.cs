using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class DocFavoritePhraseRepository
{
    public List<DocFavoritePhrase> GetAll()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Body, Category, IsFavorite FROM DocFavoritePhraseTable ORDER BY IsFavorite DESC, Category, Title";
        using var r = cmd.ExecuteReader();
        var list = new List<DocFavoritePhrase>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public void Save(DocFavoritePhrase phrase)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        if (phrase.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO DocFavoritePhraseTable (Title, Body, Category, IsFavorite) VALUES ($t, $b, $c, $f)";
            Bind(cmd, phrase);
            cmd.ExecuteNonQuery();
            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            phrase.Id = (int)(long)idCmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE DocFavoritePhraseTable SET Title=$t, Body=$b, Category=$c, IsFavorite=$f WHERE Id=$id";
            Bind(cmd, phrase);
            cmd.Parameters.AddWithValue("$id", phrase.Id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(int id)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DocFavoritePhraseTable WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static DocFavoritePhrase Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Title = r.IsDBNull(1) ? "" : r.GetString(1),
        Body = r.IsDBNull(2) ? "" : r.GetString(2),
        Category = r.IsDBNull(3) ? "일반" : r.GetString(3),
        IsFavorite = r.GetInt32(4) == 1
    };

    private static void Bind(SqliteCommand cmd, DocFavoritePhrase p)
    {
        cmd.Parameters.AddWithValue("$t", p.Title);
        cmd.Parameters.AddWithValue("$b", p.Body);
        cmd.Parameters.AddWithValue("$c", p.Category);
        cmd.Parameters.AddWithValue("$f", p.IsFavorite ? 1 : 0);
    }
}
