using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace MedicalCenter.API.Data
{
    public class LocalDbContextFactory : ILocalDbContextFactory
    {
        private readonly IConfiguration _configuration;

        public LocalDbContextFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public LocalDbContext CreateDbContext(int centroMedicoId)
        {
            string? connectionString;

            switch (centroMedicoId)
            {
                // --- AGREGAR ESTE CASO ---
                case 1: // ID de Quito (Global) actuando como Local
                    // Para el nodo 1, su base de datos "local" es la misma GlobalDb
                    connectionString = _configuration.GetConnectionString("GlobalDb");
                    break;
                // -------------------------

                case 2: // ID de Guayaquil
                    connectionString = _configuration.GetConnectionString("GuayaquilDb");
                    break;
                case 3: // ID de Cuenca
                    connectionString = _configuration.GetConnectionString("CuencaDb");
                    break;
                default:
                    throw new ArgumentException($"Centro Médico ID no válido o no tiene base de datos local: {centroMedicoId}");
            }

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"No se encontró la cadena de conexión para el Centro Médico ID: {centroMedicoId}");
            }

            var optionsBuilder = new DbContextOptionsBuilder<LocalDbContext>();
            // Usamos la cadena seleccionada
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

            return new LocalDbContext(optionsBuilder.Options);
        }
    }
}