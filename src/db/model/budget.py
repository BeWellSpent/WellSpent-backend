from datetime import date
from sqlmodel import Relationship, SQLModel, Field

class BudgetModel(SQLModel, table=True):
    __tablename__ = "budget"
    id: str | None = Field(default=None, primary_key=True)
    user_id: int | None = Field(default=None, foreign_key="users.id")
    name: str | None = Field()
    start_date: date | None = Field(default=None)
    end_date: date | None = Field(default=None)
    active: bool | None = Field(default=True)
    people_mappings: list["BudgetToPeopleModel"] = Relationship(
        back_populates="budget",
        sa_relationship_kwargs={"lazy": "selectin"}  # Always eager load
    )

class BudgetToPeopleModel(SQLModel, table=True):
    __tablename__ = "budget_to_user_mapping"
    id: int | None = Field(default=None, primary_key=True)
    budget_id: str | None = Field(default=None, foreign_key="budget.id")
    user_name: str | None = Field(default=None)
    budget: BudgetModel = Relationship(back_populates="people_mappings")
