using Dapper;
using SpoerStats2.Models;
using SpoerStats2.IRepository;
using System.Data.SqlClient;

namespace SpoerStats2.ClassRepository
{
    public class MessageRepository : IMessageRepository
    {
        private readonly string _connectionString;

        public MessageRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<TeamMessage>> GetMessagesByTeam(int teamId)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"SELECT m.Id, m.TeamId, m.SenderId, m.Content, m.CreatedAt,
                               a.Id, a.MessageId, a.FileUrl, a.FileType
                        FROM TeamMessages m
                        LEFT JOIN MessageAttachments a ON m.Id = a.MessageId
                        WHERE m.TeamId = @TeamId
                        ORDER BY m.CreatedAt ASC";

            var messageDict = new Dictionary<int, TeamMessage>();

            var list = await connection.QueryAsync<TeamMessage, MessageAttachment, TeamMessage>(
                sql,
                (msg, att) =>
                {
                    if (!messageDict.TryGetValue(msg.Id, out var currentMsg))
                    {
                        currentMsg = msg;
                        currentMsg.Attachments = new List<MessageAttachment>();
                        messageDict.Add(currentMsg.Id, currentMsg);
                    }
                    if (att != null)
                        currentMsg.Attachments.Add(att);

                    return currentMsg;
                },
                new { TeamId = teamId }
            );

            return messageDict.Values;
        }

        public async Task<int> AddMessage(TeamMessage message)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"INSERT INTO TeamMessages (TeamId, SenderId, Content, CreatedAt)
                        VALUES (@TeamId, @SenderId, @Content, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() as int);";
            return await connection.ExecuteScalarAsync<int>(sql, message);
        }

        public async Task AddAttachment(MessageAttachment attachment)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"INSERT INTO MessageAttachments (MessageId, FileUrl, FileType)
                        VALUES (@MessageId, @FileUrl, @FileType)";
            await connection.ExecuteAsync(sql, attachment);
        }

        public async Task<bool> DeleteMessage(int messageId, int userId)
        {
            using var connection = new SqlConnection(_connectionString);

            // Проверяваме дали съобщението е на този потребител
            var checkSql = "SELECT COUNT(1) FROM TeamMessages WHERE Id = @MessageId AND SenderId = @UserId";
            var exists = await connection.ExecuteScalarAsync<int>(checkSql, new { MessageId = messageId, UserId = userId });

            if (exists == 0) return false;

            // Първо трием прикачените файлове
            await connection.ExecuteAsync("DELETE FROM MessageAttachments WHERE MessageId = @MessageId", new { MessageId = messageId });

            // Трием самото съобщение
            await connection.ExecuteAsync("DELETE FROM TeamMessages WHERE Id = @MessageId", new { MessageId = messageId });

            return true;
        }
    }
}