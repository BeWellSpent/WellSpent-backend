from datetime import date
from pydantic import BaseModel, Field, ConfigDict
from src.enums.budget import RecurringType, ExpenseType, PaymentType

class BaseBudgetEditRequest(BaseModel):
    budget_id: str | None = Field(default=None, title="budget identifier")

class BudgetCreateRequest(BaseModel):
    user_id : int | None = Field(default=None, title="user identifier")
    name : str | None = Field(default=None, title="budget name")

class BudgetToPeopleMapping(BaseModel):
    model_config = ConfigDict(from_attributes=True)
    id: int | None = None
    budget_id: str | None = None
    user_name: str | None = None

class Income(BaseModel):
    Id: str
    name: str
    amount: float
    recurring: bool

class PaymentMethod(BaseModel):
    Id: str
    name: str
    payment_type: PaymentType

class Savings(BaseModel):
    Id: str
    name: str
    planned_amount: float
    actual_amount: float
    recurring: bool

class Category(BaseModel):
    Id: str
    name: str
    description: str

class Person(BaseModel):
    id: str | None = Field(default=None, title="person identifier")
    name: str | None = Field(default=None, title="person name")

class Expense(BaseModel):
    model_config = ConfigDict(from_attributes=True)
    Id: str
    name: str
    date: date
    end_date: date
    renewal_date: date
    planned_amount: float
    actual_amount: float
    owner_id: int
    recurring_type: RecurringType
    expense_type: ExpenseType
    category_id: int

class Budget(BaseModel):
    model_config = ConfigDict(from_attributes=True)
    id: str | None = None
    user_id: int | None = None
    name: str | None = None
    start_date: str | None = None
    end_date: str | None = None
    active: bool | None = None
    fixed_expenses: list[Expense]  | None = None
    variable_expenses: list[Expense]  | None = None
    savings: list[Savings]  | None = None
    payment_methods: list[PaymentMethod]  | None = None
    categories: list[Category]  | None = None
    people_mappings: list[BudgetToPeopleMapping] | None = None

class PersonRequest(BaseModel):
    id: str | None = Field(default=None, title="person identifier")
    name: str | None = Field(default=None, title="person name")

class AddPeopleToBudgetRequest(BaseBudgetEditRequest):
    people: list[Person] | None = Field(default=None, title="people involved in the budget")

class AddIncomeToBudgetRequest(BaseBudgetEditRequest):
    income: list[Income] | None = Field(default=None, title="income details")
    
class AddExpenseToBudgetRequest(BaseBudgetEditRequest):
    expense: list[Expense] | None = Field(default=None, title="expense details")
