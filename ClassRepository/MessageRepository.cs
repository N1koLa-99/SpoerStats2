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

            // 1) Съобщения + имена (JOIN към таблицата с потребители)
            // Смени [Users] с реалното име на таблицата ти (напр. AspNetUsers), и колоните FirstName/LastName според схемата ти.
            var sqlMessages = @"
        SELECT
            m.Id, m.TeamId, m.SenderId, m.Content, m.CreatedAt,
            u.FirstName AS SenderFirstName,
            u.LastName  AS SenderLastName
        FROM TeamMessages m
        LEFT JOIN [Users] u ON u.Id = m.SenderId
        WHERE m.TeamId = @TeamId
        ORDER BY m.CreatedAt ASC;";

            var messages = (await connection.QueryAsync<TeamMessage>(sqlMessages, new { TeamId = teamId }))
                            .ToList();

            if (messages.Count == 0) return messages;

            // 2) Всички прикачени за тези съобщения в един удар
            var msgIds = messages.Select(x => x.Id).ToArray();

            var sqlAtt = @"
        SELECT Id, MessageId, FileUrl, FileType
        FROM MessageAttachments
        WHERE MessageId IN @Ids
        ORDER BY Id ASC;";

            var atts = await connection.QueryAsync<MessageAttachment>(sqlAtt, new { Ids = msgIds });

            // 3) Разпределяме по съобщения
            var attLookup = atts.GroupBy(a => a.MessageId)
                                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var m in messages)
            {
                if (attLookup.TryGetValue(m.Id, out var list))
                    m.Attachments = list;
            }

            return messages;
        }


        public async Task<int> AddMessage(TeamMessage message)
        {
            using var connection = new SqlConnection(_connectionString);

            // Вариант А: подаваме CreatedAt от C# (вече е DateTimeOffset.UtcNow)
            const string sql = @"
INSERT INTO dbo.TeamMessages (TeamId, SenderId, Content, CreatedAt)
VALUES (@TeamId, @SenderId, @Content, @CreatedAt);
SELECT CAST(SCOPE_IDENTITY() as int);";

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                message.TeamId,
                message.SenderId,
                message.Content,
                message.CreatedAt // DateTimeOffset (UTC)
            });
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