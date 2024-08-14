using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using MyDriver.BuildBlock.FTP.Dtos;

namespace MyDriver.BuildBlock.FTP.Service;

public static class FileService
{
    public static async Task<FileDto> WriteFile(IFormFile file)
    {
        var result = new FileDto();
        try
        {
            result.Name = file.FileName;
            result.Size = file.Length;
            result.Type = file.ContentType;
            //IFormFile file = ConvertBase64ToIFormFile(fileString, fileName, type);
            //string fileName = string.Empty;
            var extension = "." + file.FileName.Split('.')[file.FileName.Split('.').Length - 1];
            result.StoredName = DateTime.Now.Ticks.ToString() + extension;

            //var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Upload\\Files");
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "D:\\TestProJects\\AppDrive\\BackEnd\\src\\BuidingBlocks\\MyDriver.BuildBlock.FTP\\Upload\\Files");
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }

            //var exactPath = Path.Combine(Directory.GetCurrentDirectory(), "Upload\\Files", result.StoredName);
            var exactPath = Path.Combine(Directory.GetCurrentDirectory(), "D:\\TestProJects\\AppDrive\\BackEnd\\src\\BuidingBlocks\\MyDriver.BuildBlock.FTP\\Upload\\Files", result.StoredName);
            using (var stream = new FileStream(exactPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
        }
        catch (Exception ex)
        {

            throw;
        }
        return result;
    }

    public static async Task<FileContentResult> DownloadFile(string filename)
    {

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Upload\\Files", filename);
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var result = new FileContentResult(bytes, filename);
        //return new FileContentResult(bytes, contentType, Path.GetFileName(filePath));

        return result;
    }
    public static async Task<bool> DeleteFile(string fileName)
    {
        string _rootPath = "D:\\TestProJects\\AppDrive\\BackEnd\\src\\BuidingBlocks\\MyDriver.BuildBlock.FTP\\Upload\\Files"; // Thư mục chứa các file tĩnh

        // Kết hợp đường dẫn gốc với tên file để xác định đường dẫn đầy đủ
        var filePath = Path.Combine(_rootPath, fileName);

        if (System.IO.File.Exists(filePath))
        {
            // Xóa file
            System.IO.File.Delete(filePath);
            return true;
        }
        return false;

    }

    public static async Task<object> DeleteFiles(List<string> fileNames)
    {
        string _rootPath = "D:\\TestProJects\\AppDrive\\BackEnd\\src\\BuidingBlocks\\MyDriver.BuildBlock.FTP\\Upload\\Files"; // Thư mục chứa các file tĩnh

        var tasks = fileNames.Select(async fileName =>
        {
            var filePath = Path.Combine(_rootPath, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return (fileName, deleted: true);
            }
            else
            {
                return (fileName, deleted: false);
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        var deletedFiles = results.Where(r => r.deleted).Select(r => r.fileName).ToList();
        var notFoundFiles = results.Where(r => !r.deleted).Select(r => r.fileName).ToList();


        return new
        {
            deletedFiles = deletedFiles,
            notFoundFiles = notFoundFiles
        };
    }
}

