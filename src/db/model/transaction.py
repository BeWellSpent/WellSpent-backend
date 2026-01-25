from sqlmodel import SQLModel, Field
from datetime import date

class TransactionTypeModel(SQLModel, table=True):
    __tablename__ = "transaction_type"
    id: int = Field(default=None, primary_key=True)
    name: str

class TransactionFrequencyModel(SQLModel, table=True):
    __tablename__ = "transaction_frequency"
    id: int = Field(default=None, primary_key=True)
    name: str

class TransactionModel(SQLModel):
    __tablename__ = "transaction"
    amount: float | None = Field()
    budget_id: int | None = Field(foreign_key="budget.id")
    category_id: int | None = Field(foreign_key="category.id")
    date: date | None = Field()
    id: str | None = Field(default=None, primary_key=True)
    name: str | None = Field()
    payment_method_id: int | None = Field(foreign_key="payment_method.id")
    planned_amount: float | None = Field()
    recurring: bool | None = Field()
    renewal_date: date | None = Field()
    transaction_frequency_id: int | None = Field(foreign_key="transaction_frequency.id")
    transaction_type_id: int | None = Field(foreign_key="transaction_type.id")
    