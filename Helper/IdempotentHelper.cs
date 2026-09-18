using Inventory.Msv.Models;
using MessageMQCommon.Respones;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Diagnostics;

namespace Inventory.Msv.Helper
{
    public static class IdempotentHelper
    {
        public static ServiceResult<TrxStockMutation> GetIdempotentSuccessResult(string name)
        {
            var msg = $"{name} details for this message have already been processed (Idempotent Hit).";
            var successMsg = new ServiceResult<TrxStockMutation>(true)
            {
                IsSuccess = true, // WAJIB TRUE agar Consumer mengirim ACK ke RabbitMQ
                Data = new TrxStockMutation(),
                ErrorMessage = msg,
                ErrorCode = "ALREADY_PROCESSED"
            };

            Activity.Current?.SetTag("biz.inventory.status", successMsg.ErrorMessage);
            Activity.Current?.SetTag("biz.inventory.idempotent", "true");
            return successMsg;
        }
        // Helper untuk standarisasi response error database umum
        public static ServiceResult<TrxStockMutation> GetDatabaseErrorResult(string message)
        {
            var errMsg = new ServiceResult<TrxStockMutation>(false)
            {
                IsSuccess = false,
                Data = new TrxStockMutation(),
                ErrorMessage = $"Error inserting inventory: {message}",
                ErrorCode = "DATABASE_ERROR"
            };
            Activity.Current?.SetTag("biz.inventory.status", errMsg.ErrorMessage);
            return errMsg;
        }

        // Helper untuk mendeteksi kode eror duplikat spesifik Database Engine Anda
        public static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            if (ex.InnerException is DbException dbException)
            {
                // Kode eror duplikat (Unique/Primary Key Violation):
                // - PostgreSQL: "23505"
                // - MySQL: 1062
                // - SQL Server: 2627 atau 2601
                string sqlState = dbException.SqlState;
                int errorCode = dbException.ErrorCode;

                return sqlState == "23505" || errorCode == 1062 || errorCode == 2627 || errorCode == 2601 ||
                       ex.InnerException.Message.Contains("duplicate") || ex.InnerException.Message.Contains("UniqueConstraint");
            }
            return false;
        }
    }
}
