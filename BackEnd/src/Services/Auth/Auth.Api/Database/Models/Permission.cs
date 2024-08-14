namespace Auth.Api.Database.Models;

public class Permission
{
    public Guid RoleId { get; set; }
    public Guid FunctionId { get; set; }
    public Guid ActionId { get; set; }
    public virtual AppRole Role { get; set; }
    public virtual Function Function { get; set; }
    public virtual Action Action { get; set; }

}
