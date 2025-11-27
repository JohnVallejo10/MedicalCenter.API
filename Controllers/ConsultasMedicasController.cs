using MedicalCenter.API.Data;
using MedicalCenter.API.Models.DTOs;
using MedicalCenter.API.Models.Entities; // ✨ Asegúrate de tener este using
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MedicalCenter.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ConsultasMedicasController : ControllerBase
    {
        private readonly ILocalDbContextFactory _localContextFactory;
        private readonly GlobalDbContext _globalContext;

        public ConsultasMedicasController(ILocalDbContextFactory localContextFactory, GlobalDbContext globalContext)
        {
            _localContextFactory = localContextFactory;
            _globalContext = globalContext;
        }

        // --- HELPERS ---
        private int? GetCentroIdFromToken()
        {
            var centroIdClaim = User?.FindFirst("centro_medico_id");
            if (centroIdClaim == null || !int.TryParse(centroIdClaim.Value, out var centroId))
                return null;
            return centroId;
        }

        private LocalDbContext GetContextFromToken(int centroId)
        {
            return _localContextFactory.CreateDbContext(centroId);
        }
        // --- FIN HELPERS ---

        // GET: api/ConsultasMedicas
        [HttpGet]
        [Authorize(Roles = "ADMINISTRATIVO, MEDICO")]
        public async Task<ActionResult<IEnumerable<ConsultaMedica>>> GetConsultasMedicas()
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized("Token inválido: falta centro_medico_id.");

            // Detectamos si el usuario es médico para diferenciar la lógica
            bool esMedico = User.IsInRole("MEDICO");

            // CASO 1: Si es el ADMIN CENTRAL (ID 1) Y NO ES MÉDICO
            // Solo el administrativo global quiere ver el reporte unificado de todas las sedes.
            if (centroId.Value == 1 && !esMedico)
            {
                var listaUnificada = new List<ConsultaMedica>();
                // IDs de tus nodos esclavos (Guayaquil y Cuenca)
                int[] nodosEsclavos = { 2, 3 };

                foreach (var nodoId in nodosEsclavos)
                {
                    try
                    {
                        using (var context = _localContextFactory.CreateDbContext(nodoId))
                        {
                            var consultasNodo = await context.ConsultasMedicas.ToListAsync();
                            listaUnificada.AddRange(consultasNodo);
                        }
                    }
                    catch (Exception)
                    {
                        // Si un nodo falla, continuamos con los demás
                        continue;
                    }
                }

                // Retornamos la lista combinada
                return Ok(listaUnificada.OrderByDescending(c => c.FechaHora));
            }

            // CASO 2: LÓGICA LOCAL (Médicos de cualquier centro O Admins Locales)
            // - Si es Médico del Centro 1: Cae aquí y ve la DB del Centro 1.
            // - Si es Médico del Centro 2: Cae aquí y ve la DB del Centro 2.
            using (var context = GetContextFromToken(centroId.Value))
            {
                return await context.ConsultasMedicas
                                    .OrderByDescending(c => c.FechaHora)
                                    .ToListAsync();
            }
        }

        // GET: api/ConsultasMedicas/5
        [HttpGet("{id}")]
        [Authorize(Roles = "ADMINISTRATIVO, MEDICO")]
        public async Task<ActionResult<ConsultaMedica>> GetConsultaMedica(int id)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();
            if (centroId.Value == 1) return NotFound();

            using (var context = GetContextFromToken(centroId.Value))
            {
                var consulta = await context.ConsultasMedicas.FindAsync(id);
                if (consulta == null)
                {
                    return NotFound("Consulta no encontrada en este centro médico.");
                }
                return consulta;
            }
        }

        // POST: api/ConsultasMedicas
        [HttpPost]
        [Authorize(Roles = "ADMINISTRATIVO, MEDICO")]
        public async Task<ActionResult<ConsultaMedica>> PostConsultaMedica(ConsultaMedicaCreateDto consultaDto)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            // --- BLOQUE ELIMINADO: Ya no restringimos al ID 1 ---
            // El médico del centro 1 (Global) AHORA SÍ puede guardar en su base local.

            // 1. VALIDACIÓN MANUAL EN GLOBAL
            // Verificamos que el paciente y médico existan en la base maestra antes de guardar
            var pacienteExiste = await _globalContext.Pacientes.AnyAsync(p => p.Id == consultaDto.PacienteId);
            if (!pacienteExiste)
                return BadRequest($"El Paciente con ID {consultaDto.PacienteId} no existe en la base global.");

            var medicoExiste = await _globalContext.Medicos.AnyAsync(m => m.Id == consultaDto.MedicoId);
            if (!medicoExiste)
                return BadRequest($"El Médico con ID {consultaDto.MedicoId} no existe en la base global.");

            // 2. GUARDADO EN LOCAL
            // GetContextFromToken(1) devolverá el contexto conectado a la DB local de Quito/Global
            using (var context = GetContextFromToken(centroId.Value))
            {
                var nuevaConsulta = new MedicalCenter.API.Models.Entities.ConsultaMedica
                {
                    PacienteId = consultaDto.PacienteId,
                    MedicoId = consultaDto.MedicoId,
                    Motivo = consultaDto.Motivo ?? string.Empty,
                    FechaHora = consultaDto.FechaHora ?? DateTime.Now
                };

                context.ConsultasMedicas.Add(nuevaConsulta);
                await context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetConsultaMedica), new { id = nuevaConsulta.Id }, nuevaConsulta);
            }
        }

        // PUT: api/ConsultasMedicas/5
        [HttpPut("{id}")]
        [Authorize(Roles = "ADMINISTRATIVO, MEDICO")]
        public async Task<IActionResult> PutConsultaMedica(int id, ConsultaMedica consultaMedica)
        {
            if (id != consultaMedica.Id) return BadRequest("El ID de la URL no coincide con el cuerpo.");

            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();
            if (centroId.Value == 1) return Forbid();

            using (var context = GetContextFromToken(centroId.Value))
            {
                var existe = await context.ConsultasMedicas.AnyAsync(e => e.Id == id);
                if (!existe) return NotFound();

                context.Entry(consultaMedica).State = EntityState.Modified;

                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await context.ConsultasMedicas.AnyAsync(e => e.Id == id))
                        return NotFound();
                    else
                        throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/ConsultasMedicas/5
        [HttpDelete("{id}")]
        // 1. SOLUCIÓN AL ERROR 403: Agregamos "MEDICO" a los roles permitidos
        [Authorize(Roles = "ADMINISTRATIVO, MEDICO")]
        public async Task<IActionResult> DeleteConsultaMedica(int id)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            using (var context = GetContextFromToken(centroId.Value))
            {
                // Buscar la consulta
                var consulta = await context.ConsultasMedicas.FindAsync(id);
                if (consulta == null) return NotFound();

                // 2. SOLUCIÓN PREVENTIVA AL ERROR 500 (Foreign Key):
                // Antes de borrar la consulta, debemos borrar sus diagnósticos y prescripciones asociadas.
                // Como no tienes la propiedad de navegación 'Diagnosticos' en tu modelo ConsultaMedica,
                // los buscamos manualmente:

                var diagnosticosAsociados = await context.Diagnosticos
                    .Where(d => d.ConsultaId == id)
                    .Include(d => d.Prescripciones) // Incluimos nietos (prescripciones)
                    .ToListAsync();

                foreach (var diagnostico in diagnosticosAsociados)
                {
                    // Borrar prescripciones del diagnóstico
                    if (diagnostico.Prescripciones != null && diagnostico.Prescripciones.Any())
                    {
                        context.Prescripciones.RemoveRange(diagnostico.Prescripciones);
                    }
                }

                // Borrar los diagnósticos
                if (diagnosticosAsociados.Any())
                {
                    context.Diagnosticos.RemoveRange(diagnosticosAsociados);
                }

                // 3. Finalmente borramos la consulta (ahora limpia de hijos)
                context.ConsultasMedicas.Remove(consulta);
                await context.SaveChangesAsync();
            }

            return NoContent();
        }
    }
}