from enum import Enum
from pydantic import BaseModel
from datetime import date
import uuid

class Budget(BaseModel):
    Id: uuid.UUID
    name: str
    fixed_expenses: list[Expense]
    variable_expenses: list[Expense]
    savings: list[Savings]
    payment_methods: list[PaymentMethod]
    categories: list[Category]
    start_date: date
    end_date: date

class Income(BaseModel):
    Id: uuid.UUID
    name: str
    amount: float
    recurring: bool

class Expense(BaseModel):
    Id: uuid.UUID
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

class PaymentMethod(BaseModel):
    Id: uuid.UUID
    name: str
    payment_type: PaymentType

class Savings(BaseModel):
    Id: uuid.UUID
    name: str
    planned_amount: float
    actual_amount: float
    recurring: bool

class Category(BaseModel):
    Id: uuid.UUID
    name: str
    description: str

class RecurringType(Enum):
    ONE_OFF = 1
    WEEKLY = 2
    BI_WEEKLY = 3
    MONTHLY = 4
    YEARLY = 5

class ExpenseType(Enum):
    FIXED = 1
    VARIABLE = 2

class PaymentType(Enum):
    CASH = 1
    CREDIT = 2
    DEBIT = 3
    DIGITAL_WALLET = 4
    BANK_TRANSFER = 5
    CRYPTO = 6
    INVESTMENT = 7
    OTHER = 99
    