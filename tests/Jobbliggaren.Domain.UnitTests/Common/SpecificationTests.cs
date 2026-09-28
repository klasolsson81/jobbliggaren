using System.Linq.Expressions;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Common;

public class SpecificationTests
{
    [Fact]
    public void IsSatisfiedByAndCriteria_BuiltFromOneExpression_Agree()
    {
        Expression<Func<int, bool>> criteria = n => n > 2;

        var specification = new Specification<int>(criteria);

        specification.Criteria.ShouldBeSameAs(criteria);
        specification.IsSatisfiedBy(3).ShouldBeTrue();
        specification.IsSatisfiedBy(2).ShouldBeFalse();
    }
}
