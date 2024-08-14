namespace Auth.Api.Database.Models;

public class ActionInFunction
{
    public Guid ActionId { get; set; }
    public Guid FunctionId { get; set; }

    public virtual Action Action { get; set; }
    public virtual Function Function { get; set; }
}