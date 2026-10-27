using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace _23DTHD5_Demo.Controllers.api
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoTuongController : ControllerBase
    {
        private IWebHostEnvironment _environment;

        public CoTuongController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }
        [HttpGet("board")]
        public  IActionResult GetBoard()
        {
            try
            {
                var filePath = Path.Combine(
                    _environment.WebRootPath,
                    "co_tuong_initial.json"
                );
                var json = System.IO.File.ReadAllText(filePath);

                return Ok(new { status = true, message = "", data = json});
            }
            catch (Exception ex) {
                return Ok(new { status = false, message = "có lỗi xảy ra. tụi em tự định nghĩa" });
            }
            
        }
    }
}
