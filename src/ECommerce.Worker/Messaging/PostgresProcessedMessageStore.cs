using ECommerce.Worker.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ECommerce.Worker.Messaging;

public sealed class PostgresProcessedMessageStore(IOptions<PostgresOptions> options) : IProcessedMessageStore
{
    private readonly string _connectionString = options.Value.ConnectionString;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS worker_processed_messages (
                message_id uuid PRIMARY KEY,
                event_type varchar(100) NOT NULL,
                processed_at timestamptz NOT NULL
            );
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM worker_processed_messages WHERE message_id = $1);";
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(messageId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task MarkProcessedAsync(
        Guid messageId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO worker_processed_messages (message_id, event_type, processed_at)
            VALUES ($1, $2, now())
            ON CONFLICT (message_id) DO NOTHING;
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(messageId);
        command.Parameters.AddWithValue(eventType);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
