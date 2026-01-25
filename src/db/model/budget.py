from sqlmodel import SQLModel, Field

class BudgetModel(SQLModel, table=True):
    __tablename__ = "budget"
    id: int | None = Field(default=None, primary_key=True)
    name: str | None = Field()
    enabled: bool | None = Field(default=True)
    parent: bool | None = Field(default=False)
