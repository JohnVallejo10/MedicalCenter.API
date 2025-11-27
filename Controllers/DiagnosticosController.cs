using MedicalCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Collections.Generic;

namespace MedicalCenter.API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DiagnosticosController : ControllerBase
    {
        private readonly ILocalDbContextFactory _localContextFactory;

        public DiagnosticosController(ILocalDbContextFactory localContextFactory)
        {
            _localContextFactory = localContextFactory;
        }

        private int? GetCentroIdFromToken()
        {
            var centroIdClaim = User.FindFirst("centro_medico_id");
            if (centroIdClaim == null || !int.TryParse(centroIdClaim.Value, out var centroId))
                return null;
            return centroId;
        }

        private LocalDbContext GetContextFromToken(int centroId)
        {
            return _localContextFactory.CreateDbContext(centroId);
        }

        // GET: api/Diagnosticos
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Diagnostico>>> GetDiagnosticos()
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            // CORRECCIÓN: Eliminamos el bloqueo al ID 1. 
            // Ahora Carolina podrá ver la lista si tiene diagnósticos.
            using (var _context = GetContextFromToken(centroId.Value))
            {
                return await _context.Diagnosticos.ToListAsync();
            }
        }

        // GET: api/Diagnosticos/PorConsulta/5
        [HttpGet("PorConsulta/{consultaId}")]
        public async Task<ActionResult<IEnumerable<Diagnostico>>> GetDiagnosticosPorConsulta(int consultaId)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            // CORRECCIÓN: Eliminamos el bloqueo al ID 1.
            using (var _context = GetContextFromToken(centroId.Value))
            {
                var consultaExiste = await _context.ConsultasMedicas.AnyAsync(c => c.Id == consultaId);
                if (!consultaExiste)
                    return NotFound("La consulta no existe en este centro médico.");

                return await _context.Diagnosticos
                    .Where(d => d.ConsultaId == consultaId)
                    .ToListAsync();
            }
        }

        // GET: api/Diagnosticos/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Diagnostico>> GetDiagnostico(int id)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            using (var _context = GetContextFromToken(centroId.Value))
            {
                var diagnostico = await _context.Diagnosticos.FindAsync(id);
                if (diagnostico == null) return NotFound();
                return diagnostico;
            }
        }

        // PUT: api/Diagnosticos/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutDiagnostico(int id, Diagnostico diagnostico)
        {
            if (id != diagnostico.Id) return BadRequest();

            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            // CORRECCIÓN: Se eliminó el "return Forbid(...)" que causaba el error 500

            using (var _context = GetContextFromToken(centroId.Value))
            {
                var existe = await _context.Diagnosticos.AnyAsync(e => e.Id == id);
                if (!existe) return NotFound();

                _context.Entry(diagnostico).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return NoContent();
        }

        // POST: api/Diagnosticos
        [HttpPost]
        public async Task<ActionResult<Diagnostico>> PostDiagnostico(Diagnostico diagnostico)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            // CORRECCIÓN: Eliminado el bloqueo y el error de sintaxis Forbid()
            // Ahora Carolina (Centro 1) puede guardar diagnósticos.

            using (var _context = GetContextFromToken(centroId.Value))
            {
                var consultaExiste = await _context.ConsultasMedicas.AnyAsync(c => c.Id == diagnostico.ConsultaId);
                if (!consultaExiste)
                    return BadRequest(new { message = "El ConsultaId no existe en este centro médico." });

                _context.Diagnosticos.Add(diagnostico);
                await _context.SaveChangesAsync();

                return CreatedAtAction("GetDiagnostico", new { id = diagnostico.Id }, diagnostico);
            }
        }

        // DELETE: api/Diagnosticos/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDiagnostico(int id)
        {
            var centroId = GetCentroIdFromToken();
            if (!centroId.HasValue) return Unauthorized();

            // CORRECCIÓN: Eliminado el bloqueo al admin global/médico local

            using (var _context = GetContextFromToken(centroId.Value))
            {
                var diagnostico = await _context.Diagnosticos
                    .Include(d => d.Prescripciones)
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (diagnostico == null) return NotFound();

                if (diagnostico.Prescripciones != null && diagnostico.Prescripciones.Any())
                {
                    _context.Prescripciones.RemoveRange(diagnostico.Prescripciones);
                }

                _context.Diagnosticos.Remove(diagnostico);
                await _context.SaveChangesAsync();

                return NoContent();
            }
        }
    }
}