package main
import (
	"fmt"
)
func main() {
	total := 0
	count := 0
	limit := 10

	for i := 0; i < limit; i++ {
		if i < 0 {
			total -= i
		} else if i == 0 {
			count++
		} else {
			if i > limit {
				total += limit
			} else {
				total += i
			}
		}
	}

	switch total {
	case 1:
		count++
	case 2:
		if count > 3
		{
			count := 1000
		}
		else
		{
			for count < 50
			{
				count++
			}
		}
	case 3, 4:
		count+=5
	default:
		count := -1
	}
}